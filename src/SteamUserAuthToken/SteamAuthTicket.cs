using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Force.Crc32;
using SteamKit2;
using SteamKit2.Internal;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Implements generating auth session tickets (same as Steamworks <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">GetAuthSessionTicket</see>) by adding handlers to existing SteamKit session.
    /// <para>Why would You use this over <see cref="SteamHandling"/>? Because it gives You full control over steam login process.</para>
    /// </summary>
	public sealed class SteamAuthTicket : IDisposable
	{
        private TaskCompletionSource<bool> GameConnectCompleteTaskSource { get; } = new TaskCompletionSource<bool>();
		private ConcurrentQueue<byte[]> GameConnectTokens { get; } = new ConcurrentQueue<byte[]>();
		private ConcurrentDictionary<uint, List<CMsgAuthTicket>> TicketsByGame { get; } = new ConcurrentDictionary<uint, List<CMsgAuthTicket>>();

        /// <summary>
        /// Adds handler for authorization token on <paramref name="client"/> and subscribes to necessary events with <paramref name="manager"/>.
        /// </summary>
        /// <param name="client">Client to add handler on.</param>
        /// <param name="manager">Callback manager to subscribe for events on.</param>
		public SteamAuthTicket(SteamClient client, CallbackManager manager)
		{
			_client = client;
			_apps = _client.GetHandler<SteamApps>();
			_client.AddHandler(new TicketAuth());

			_onGameConnectTokens = manager.Subscribe<SteamApps.GameConnectTokensCallback>(OnGameConnectTokens);
            _onTicketAuthComplete = manager.Subscribe<TicketAuthCompleteCallback>(OnTicketAuthComplete);
		}

        /// <summary>
        /// Performs <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">session ticket</see> generation and validation for specified <paramref name="gameID"/>. 
        /// </summary>
        /// <param name="gameID">Game to generate ticket for.</param>
        /// <returns>Bytes representing the session ticket, or empty <see cref="ReadOnlyMemory{T}.Empty"/> if You don't own the game.</returns>
        public async Task<ReadOnlyMemory<byte>> GetAuthSessionTicket(GameID gameID)
		{
            ReadOnlyMemory<byte> BuildToken(byte[] authToken, byte[] appTicket)
            {
                var len = appTicket.Length;
                Memory<byte> token = new byte[authToken.Length + 4 + len];
                authToken.CopyTo(token);
                MemoryMarshal.Write(token.Slice(authToken.Length).Span, ref len);
                appTicket.CopyTo(token.Slice(authToken.Length + 4));
                return token;
            }
            byte[] CreateAuthTicket(byte[] gameConnectToken)
            {
                const int sessionSize =
                    4 + // unknown 1
                    4 + // unknown 2
                    4 + // external IP
                    4 + // filler
                    4 + // timestamp
                    4;  // connection count

                using (var stream = new MemoryStream(gameConnectToken.Length + 4 + sessionSize))
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.Write(gameConnectToken.Length);
                        writer.Write(gameConnectToken.ToArray());

                        writer.Write(sessionSize);
                        writer.Write(1);
                        writer.Write(2);

                        writer.Write(0 /* IP address */);
                        writer.Write(0 /* padding */);
                        writer.Write(2038 /* ms since connected to steam? */);
                        writer.Write(1 /* connection count to steam? */);
                    }

                    return stream.ToArray();
                }
            }

            // make sure we received GameConnect tokens
            await WaitForInitialization();

            if (GameConnectTokens.TryDequeue(out var token))
			{
				byte[] authToken = CreateAuthTicket(token);
                var ticketTask = VerifyTicket(gameID.AppID, authToken, out var crc).ToTask();
                var appTicketTask = _apps.GetAppOwnershipTicket(gameID.AppID).ToTask();
                await Task.WhenAll(ticketTask, appTicketTask);

                var callback = await ticketTask;
				if (callback.ActiveTicketsCRC.Any(x => x == crc))
				{
                    var appTicket = await appTicketTask;
					if (appTicket.Result == EResult.OK)
						return BuildToken(authToken, appTicket.Ticket);
				}
			}
			return ReadOnlyMemory<byte>.Empty;
		}
        /// <summary>
        /// Wait for steam to accept login and send necessary data for token generation.
        /// </summary>
        /// <remarks>If not awaited, it's done by the <see cref="GetAuthSessionTicket(GameID)"/> method</remarks>
        /// <returns></returns>
        public Task WaitForInitialization() => GameConnectCompleteTaskSource.Task;

        private void OnTicketAuthComplete(TicketAuthCompleteCallback obj)
        {
            // called when ticket was used
            return;
        }
        private void OnGameConnectTokens(SteamApps.GameConnectTokensCallback obj)
		{
			foreach (var tok in obj.Tokens)
				GameConnectTokens.Enqueue(tok);

			while (GameConnectTokens.Count > obj.TokensToKeep)
				GameConnectTokens.TryDequeue(out _);

            GameConnectCompleteTaskSource.TrySetResult(true);
		}

		private AsyncJob<TicketAcceptedCallback> VerifyTicket(uint appID, byte[] authToken, out uint crc)
		{
			crc = Crc32Algorithm.Compute(authToken, 0, authToken.Length);
			var items = TicketsByGame.GetOrAdd(appID, new List<CMsgAuthTicket>());
			items.Add(new CMsgAuthTicket
			{
				gameid = appID,
				ticket = authToken,
				ticket_crc = crc
			});
			return SendTickets();
		}
		private AsyncJob<TicketAcceptedCallback> SendTickets()
		{
			var auth = new ClientMsgProtobuf<CMsgClientAuthList>(EMsg.ClientAuthList, 64);
			auth.Body.tokens_left = (uint)GameConnectTokens.Count;
			auth.Body.app_ids.AddRange(TicketsByGame.Keys);
			auth.Body.tickets.AddRange(TicketsByGame.Values.SelectMany(x => x));
			auth.SourceJobID = _client.GetNextJobID();
			_client.Send(auth);
			return new AsyncJob<TicketAcceptedCallback>(_client, auth.SourceJobID);
		}

        /// <summary>
        /// Cleans up allocated resources.
        /// </summary>
        public void Dispose()
        {
            _onGameConnectTokens.Dispose();
            _onTicketAuthComplete.Dispose();
        }

        private SteamClient _client;
		private SteamApps _apps;
		private IDisposable _onGameConnectTokens;
        private IDisposable _onTicketAuthComplete;
    }
}

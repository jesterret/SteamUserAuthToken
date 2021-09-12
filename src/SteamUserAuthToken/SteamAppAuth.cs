using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Force.Crc32;
using SteamKit2;
using SteamKit2.Internal;
using SteamUserAuthToken.UserData;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Handler for generating auth session tickets (same as Steamworks <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">GetAuthSessionTicket</see>).
    /// </summary>
	public sealed partial class SteamAppAuth : ClientMsgHandler
    {
        internal uint PublicIP { get; private set; }
        internal uint LocalIP { get; private set; }

        private ConcurrentQueue<byte[]> GameConnectTokens { get; } = new ConcurrentQueue<byte[]>();
        private ConcurrentDictionary<uint, List<CMsgAuthTicket>> TicketsByGame { get; } = new ConcurrentDictionary<uint, List<CMsgAuthTicket>>();

        /// <summary>
        /// Initializes all necessary callbacks.
        /// </summary>
        public SteamAppAuth()
        {
            _dispatchMap = new()
            {
                // Save IP addresses
                {
                    EMsg.ClientLogon,
                    HandleLogon
                },
                // Save GameConnect tokens
                {
                    EMsg.ClientGameConnectTokens,
                    HandleGameConnectTokens
                },
                {
                    EMsg.ClientAuthListAck,
                    HandleAuthListAcknowledged
                },
                {
                    EMsg.ClientTicketAuthComplete,
                    HandleTicketAuthComplete
                }
            };
            _authTicketBuilder = new DefaultAuthTicketBuilder(new RandomUserDataSerializer());
        }
        /// <summary>
        /// Initialize the class, and override default auth ticket builder.
        /// </summary>
        /// <param name="authTicketBuilder">Auth ticket builder.</param>
        public SteamAppAuth(IAuthTicketBuilder authTicketBuilder) : this() => _authTicketBuilder = authTicketBuilder;
        /// <summary>
        /// Initialize the class, and override default user data serializer in auth building process.
        /// </summary>
        /// <param name="userDataSerializer">Serializer of user data.</param>
        public SteamAppAuth(IUserDataSerializer userDataSerializer) : this(new DefaultAuthTicketBuilder(userDataSerializer)) { }

        private static byte[] BuildToken(byte[] authTicket, byte[] appTicket)
        {
            var len = appTicket.Length;
            var token = new byte[authTicket.Length + 4 + len];
            var mem = token.AsSpan();
            authTicket.CopyTo(mem);
            MemoryMarshal.Write(mem.Slice(authTicket.Length), ref len);
            appTicket.CopyTo(mem.Slice(authTicket.Length + 4));
            return token;
        }

        /// <summary>
        /// Performs <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">session ticket</see> generation and validation for specified <paramref name="appid"/>. 
        /// </summary>
        /// <param name="appid">Game to generate ticket for.</param>
        public async Task<AppAuthTicket?> GetAuthSessionTicket(uint appid)
        {
            var apps = Client.GetHandler<SteamApps>();
            if (apps is not null && GameConnectTokens.TryDequeue(out var token))
            {
                var authTicket = _authTicketBuilder.Build(token);
                var ticket = await VerifyTicket(appid, authTicket, out var crc);
                var appTicket = await apps.GetAppOwnershipTicket(appid);

                // verify just in case
                if (ticket.TicketCRCs.Any(x => x == crc) && appTicket.Result == EResult.OK)
                {
                    var tok = BuildToken(authTicket, appTicket.Ticket);
                    return new AppAuthTicket(this, appid, tok);
                }
            }
            return null;
        }

        /// <summary>
        /// Performs <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">session ticket</see> generation and validation for specified <paramref name="appid"/> and <paramref name="appOwnershipTicket"/>. 
        /// </summary>
        /// <param name="appid">Game to generate ticket for.</param>
        /// <param name="appOwnershipTicket">Ownership Ticket generated for specified <paramref name="appid"/>.</param>
        /// <param name="ticket">Generated auth ticket, or <see langword="null"/>.</param>
        public AsyncJob<TicketAckCallback>? GetAuthSessionTicket(uint appid, byte[] appOwnershipTicket, out AppAuthTicket? ticket)
        {
            if (GameConnectTokens.TryDequeue(out var token))
            {
                var authTicket = _authTicketBuilder.Build(token);
                var authToken = BuildToken(authTicket, appOwnershipTicket);
                ticket = new AppAuthTicket(this, appid, authToken);
                return VerifyTicket(appid, authToken, out _);
            }
            ticket = null;
            return null;
        }

        /// <summary>
        /// Performs <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">session ticket</see> generation and validation for provided <paramref name="callback"/> app. 
        /// </summary>
        /// <param name="callback">Application ownership ticket.</param>
        /// <param name="ticket">Generated auth ticket, or <see langword="null"/>.</param>
        public AsyncJob<TicketAckCallback>? GetAuthSessionTicket(SteamApps.AppOwnershipTicketCallback callback, out AppAuthTicket? ticket) => GetAuthSessionTicket(callback.AppID, callback.Ticket, out ticket);

        internal void CancelAuthTicket(AppAuthTicket authTicket)
        {
            lock (_ticketChangeLock)
            {
                if (TicketsByGame.TryGetValue(authTicket.AppID, out var tickets))
                {
                    tickets.RemoveAll(x => x.ticket_crc == authTicket.TicketCRC);
                }
            }
            SendTickets();
        }

        private void HandleLogon(IPacketMsg msg)
        {
            var logon = new ClientMsgProtobuf<CMsgClientLogon>(msg).Body;
            var ip = Client.LocalIP ?? IPAddress.Any;
            var bytes = ip.MapToIPv4().GetAddressBytes();
            LocalIP = MemoryMarshal.Read<uint>(bytes);
            PublicIP = logon.public_ip.v4;
        }
        private void HandleGameConnectTokens(IPacketMsg msg)
        {
            var obj = new ClientMsgProtobuf<CMsgClientGameConnectTokens>(msg).Body;

            foreach (var tok in obj.tokens)
                GameConnectTokens.Enqueue(tok);

            while (GameConnectTokens.Count > obj.max_tokens_to_keep)
                GameConnectTokens.TryDequeue(out _);
        }
        private void HandleAuthListAcknowledged(IPacketMsg msg)
        {
            var ack = new ClientMsgProtobuf<CMsgClientAuthListAck>(msg);
            var callback = new TicketAckCallback(ack.TargetJobID, ack.Body);
            Client.PostCallback(callback);
        }
        private void HandleTicketAuthComplete(IPacketMsg msg)
        {
            var auth = new ClientMsgProtobuf<CMsgClientTicketAuthComplete>(msg);
            var callback = new AuthCompleteCallback(auth.TargetJobID, auth.Body);
            Client.PostCallback(callback);
        }

        private AsyncJob<TicketAckCallback> VerifyTicket(uint appID, byte[] authTicket, out uint crc)
        {
            crc = Crc32Algorithm.Compute(authTicket, 0, authTicket.Length);
            lock (_ticketChangeLock)
            {
                var items = TicketsByGame.GetOrAdd(appID, new List<CMsgAuthTicket>());
                items.Add(new CMsgAuthTicket
                {
                    gameid = appID,
                    ticket = authTicket,
                    ticket_crc = crc
                });
            }
            return SendTickets();
        }
        private AsyncJob<TicketAckCallback> SendTickets()
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>(EMsg.ClientAuthList);
            auth.Body.tokens_left = (uint)GameConnectTokens.Count;
            lock (_ticketChangeLock)
            {
                auth.Body.app_ids.AddRange(TicketsByGame.Keys);
                // flatten dictionary into ticket list
                auth.Body.tickets.AddRange(TicketsByGame.Values.SelectMany(x => x));
            }
            auth.SourceJobID = Client.GetNextJobID();
            Client.Send(auth);
            return new AsyncJob<TicketAckCallback>(Client, auth.SourceJobID);
        }

        /// <inheritdoc/>
        public override void HandleMsg(IPacketMsg packetMsg)
        {
            if (_dispatchMap.TryGetValue(packetMsg.MsgType, out var handler))
                handler(packetMsg);
        }

        private readonly Dictionary<EMsg, Action<IPacketMsg>> _dispatchMap;
        private readonly object _ticketChangeLock = new();
        private readonly IAuthTicketBuilder _authTicketBuilder;
    }
}

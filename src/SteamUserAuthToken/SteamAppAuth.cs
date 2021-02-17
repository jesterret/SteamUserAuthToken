using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Force.Crc32;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.Internal;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Handler for generating auth session tickets (same as Steamworks <see href="https://partner.steamgames.com/doc/api/ISteamUser#GetAuthSessionTicket">GetAuthSessionTicket</see>).
    /// </summary>
	public sealed partial class SteamAppAuth : ClientMsgHandler
    {
        private ConcurrentQueue<byte[]> GameConnectTokens { get; } = new ConcurrentQueue<byte[]>();
		private ConcurrentDictionary<uint, List<CMsgAuthTicket>> TicketsByGame { get; } = new ConcurrentDictionary<uint, List<CMsgAuthTicket>>();

        /// <summary>
        /// Initializes all necessary callbacks.
        /// </summary>
        public SteamAppAuth()
		{
            dispatchMap = new()
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
		}

        private static byte[] BuildToken(byte[] authToken, byte[] appTicket)
        {
            var len = appTicket.Length;
            var token = new byte[authToken.Length + 4 + len];
            var mem = token.AsSpan();
            authToken.CopyTo(mem);
            MemoryMarshal.Write(mem.Slice(authToken.Length), ref len);
            appTicket.CopyTo(mem.Slice(authToken.Length + 4));
            return token;
        }
        private byte[] CreateAuthTicket(byte[] gameConnectToken)
        {
            static uint ObfuscateIP(uint value)
            {
                var temp = 0x85EBCA6B * (value ^ (value >> 16));
                var outvalue = 0xC2B2AE35 * (temp ^ (temp >> 13));
                return outvalue ^ (outvalue >> 16);
            }

            const int sessionSize =
                4 + // unknown, always 1
                4 + // unknown, always 2
                4 + // public IP v4, optional
                4 + // private IP v4, optional
                4 + // timestamp & uint.MaxValue
                4;  // sequence

            using (var stream = new MemoryStream(gameConnectToken.Length + 4 + sessionSize))
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(gameConnectToken.Length);
                    writer.Write(gameConnectToken.ToArray());

                    writer.Write(sessionSize);
                    writer.Write(1);
                    writer.Write(2);

                    writer.Write(ObfuscateIP(_publicIP));
                    writer.Write(ObfuscateIP(_localIP));
                    writer.Write((uint)Stopwatch.GetTimestamp());
                    writer.Write(++_sequence);
                    return stream.ToArray();
                }
            }
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
                var authToken = CreateAuthTicket(token);
                var ticket = await VerifyTicket(appid, authToken, out var crc);
                var appTicket = await apps.GetAppOwnershipTicket(appid);

                // verify just in case
                if (ticket.TicketCRCs.Any(x => x == crc) && appTicket.Result == EResult.OK)
                {
                    var tok = BuildToken(authToken, appTicket.Ticket);
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
                var authTicket = CreateAuthTicket(token);
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
        public AsyncJob<TicketAckCallback>? GetAuthSessionTIcket(SteamApps.AppOwnershipTicketCallback callback, out AppAuthTicket? ticket) => GetAuthSessionTicket(callback.AppID, callback.Ticket, out ticket);

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
            _localIP = MemoryMarshal.Read<uint>(bytes);
            _publicIP = logon.public_ip.v4;
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
            if(dispatchMap.TryGetValue(packetMsg.MsgType, out var handler))
            {
                handler(packetMsg);
            }
        }

        private readonly Dictionary<EMsg, Action<IPacketMsg>> dispatchMap;
        private uint _publicIP;
        private uint _localIP;
        private uint _sequence;
        private readonly object _ticketChangeLock = new();
    }
}

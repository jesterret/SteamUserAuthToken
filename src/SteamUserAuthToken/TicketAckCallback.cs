using System.Collections.Generic;
using SteamKit2;
using SteamKit2.Internal;

namespace SteamUserAuthToken
{
    public sealed partial class SteamAppAuth
    {
        /// <summary>
        /// This callback is fired when Steam acknowledges our auth ticket.
        /// </summary>
        public class TicketAckCallback : CallbackMsg
        {
            /// <summary>
            /// <see cref="List{T}"/> of AppIDs of the games that have generated tickets.
            /// </summary>
            public List<uint> AppIDs { get; }
            /// <summary>
            /// <see cref="List{T}"/> of CRC32 hashes of activated tickets.
            /// </summary>
            public List<uint> TicketCRCs { get; }
            /// <summary>
            /// Number of message in sequence.
            /// </summary>
            public uint MessageSequence { get; }

            internal TicketAckCallback(JobID targetJobID, CMsgClientAuthListAck body)
            {
                JobID = targetJobID;
                AppIDs = body.app_ids;
                TicketCRCs = body.ticket_crc;
                MessageSequence = body.message_sequence;
            }
        }
    }
}
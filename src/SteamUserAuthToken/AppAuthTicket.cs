using System;
using Force.Crc32;

namespace SteamUserAuthToken
{
    public sealed partial class SteamAppAuth
    {
        /// <summary>
        /// Represents validated user authentication ticket.
        /// </summary>
        public class AppAuthTicket : IDisposable
        {
            internal uint TicketCRC { get; }

            /// <summary>
            /// Application the ticket was generated for.
            /// </summary>
            public uint AppID { get; }
            /// <summary>
            /// Validated session ticket.
            /// </summary>
            public byte[] Ticket { get; }

            internal AppAuthTicket(SteamAppAuth handler, uint appID, byte[] ticket)
            {
                _handler = handler;
                AppID = appID;
                Ticket = ticket;
                TicketCRC = Crc32Algorithm.Compute(ticket);
            }

            /// <summary>
            /// Discards the ticket.
            /// </summary>
            public void Dispose() => _handler.CancelAuthTicket(this);

            private readonly SteamAppAuth _handler;
        }
    }
}

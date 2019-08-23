﻿using SteamKit2;
using SteamKit2.Internal;
using System.Collections.Generic;

namespace SteamUserAuthToken
{
    /// <summary>
    /// This callback is fired when Steam accepts our auth ticket.
    /// </summary>
    public class TicketAcceptedCallback : CallbackMsg
    {
        /// <summary>
        /// <see cref="List{T}"/> of AppIDs of the games that have generated tickets.
        /// </summary>
        public List<uint> AppIDs { get; private set; }
        /// <summary>
        /// <see cref="List{T}"/> of CRC32 hashes of activated tickets.
        /// </summary>
        public List<uint> ActiveTicketsCRC { get; private set; }
        /// <summary>
        /// Number of message in sequence.
        /// </summary>
        public uint MessageSequence { get; private set; }

        internal TicketAcceptedCallback(JobID jobId, CMsgClientAuthListAck body)
        {
            JobID = jobId;
            AppIDs = body.app_ids;
            ActiveTicketsCRC = body.ticket_crc;
            MessageSequence = body.message_sequence;
        }
    }
}
using SteamKit2;
using SteamKit2.Internal;
using System;

namespace SteamUserAuthToken
{
    /// <summary>
    /// This callback is fired when generated ticket was successfully used.
    /// </summary>
    public class TicketAuthCompleteCallback : CallbackMsg
    {
        /// <summary>
        /// Steam response to authentication request.
        /// </summary>
        public EAuthSessionResponse AuthSessionResponse { get; }
        /// <summary>
        /// Authentication state.
        /// </summary>
        public uint State { get; }
        /// <summary>
        /// ID of the game the token was generated for.
        /// </summary>
        public GameID GameID { get; }
        /// <summary>
        /// <see cref="SteamKit2.SteamID"/> of the game owner.
        /// </summary>
        public SteamID OwnerSteamID { get; }
        /// <summary>
        /// <see cref="SteamKit2.SteamID"/> of the requesting user.
        /// </summary>
        public SteamID SteamID { get; }
        /// <summary>
        /// CRC of the ticket.
        /// </summary>
        public uint TicketCRC { get; }
        /// <summary>
        /// Sequence of the ticket.
        /// </summary>
        public uint TicketSequence { get; }

        internal TicketAuthCompleteCallback(JobID targetJobID, CMsgClientTicketAuthComplete body)
        {
            JobID = targetJobID;
            AuthSessionResponse = (EAuthSessionResponse)body.eauth_session_response;
            State = body.estate;
            GameID = body.game_id;
            OwnerSteamID = body.owner_steam_id;
            SteamID = body.steam_id;
            TicketCRC = body.ticket_crc;
            TicketSequence = body.ticket_sequence;
        }
    }
}
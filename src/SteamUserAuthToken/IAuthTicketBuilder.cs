namespace SteamUserAuthToken
{
    /// <summary>
    /// Handles building auth tickets.
    /// </summary>
    public interface IAuthTicketBuilder
    {
        /// <summary>
        /// Builds auth ticket with specified <paramref name="gameConnectToken"/>.
        /// </summary>
        /// <param name="gameConnectToken">Valid GameConnect token.</param>
        /// <returns>Bytes of auth ticket to send to steam servers.</returns>
        byte[] Build(byte[] gameConnectToken);
    }
}
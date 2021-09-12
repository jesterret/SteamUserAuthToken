using System.Diagnostics;
using System.IO;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Handles generation of auth ticket.
    /// </summary>
    public class DefaultAuthTicketBuilder : IAuthTicketBuilder
    {
        /// <summary>
        /// Creates instance of auth ticket builder, with specified <paramref name="userDataSerializer"/> being used.
        /// </summary>
        /// <param name="userDataSerializer"></param>
        public DefaultAuthTicketBuilder(IUserDataSerializer userDataSerializer) => _userDataSerializer = userDataSerializer;

        /// <inheritdoc/>
        public byte[] Build(byte[] gameConnectToken)
        {

            const int sessionSize =
                4 + // unknown, always 1
                4 + // unknown, always 2
                4 + // public IP v4, optional
                4 + // private IP v4, optional
                4 + // timestamp & uint.MaxValue
                4;  // sequence

            using var stream = new MemoryStream(gameConnectToken.Length + 4 + sessionSize);
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(gameConnectToken.Length);
                writer.Write(gameConnectToken);

                writer.Write(sessionSize);
                writer.Write(1);
                writer.Write(2);

                _userDataSerializer.Serialize(writer);
                writer.Write((uint)Stopwatch.GetTimestamp());
                writer.Write(++_sequence);
            }
            return stream.ToArray();
        }

        private uint _sequence;
        private readonly IUserDataSerializer _userDataSerializer;
    }
}

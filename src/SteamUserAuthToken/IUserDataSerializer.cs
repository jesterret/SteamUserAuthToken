using System.IO;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Handles serialization of user specific data in auth token generation process
    /// </summary>
    public interface IUserDataSerializer
    {
        /// <summary>
        /// Writes user data into provided buffer.
        /// </summary>
        /// <param name="writer">Buffer to write data into.</param>
        void Serialize(BinaryWriter writer);
    }
}
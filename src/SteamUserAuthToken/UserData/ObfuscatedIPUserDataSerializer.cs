using System.IO;

namespace SteamUserAuthToken.UserData
{
    internal class ObfuscatedIPUserDataSerializer : IUserDataSerializer
    {
        public ObfuscatedIPUserDataSerializer(SteamAppAuth steamAuth) => _steamAuth = steamAuth;

        public void Serialize(BinaryWriter writer)
        {
            static uint ObfuscateIP(uint value)
            {
                var temp = 0x85EBCA6B * (value ^ (value >> 16));
                var outvalue = 0xC2B2AE35 * (temp ^ (temp >> 13));
                return outvalue ^ (outvalue >> 16);
            }

            writer.Write(ObfuscateIP(_steamAuth.PublicIP));
            writer.Write(ObfuscateIP(_steamAuth.LocalIP));
        }

        private readonly SteamAppAuth _steamAuth;
    }
}

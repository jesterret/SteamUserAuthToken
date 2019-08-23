using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using SteamKit2;
#nullable enable

namespace SteamUserAuthToken
{
    /// <summary>
    /// Provides steam login information, adding convenience handlers that can be used to avoid boilerplate login code.
    /// </summary>
    public abstract class SteamCredentials
    {
        /// <summary>
        /// Username associated with <see cref="LoginKey"/>.
        /// </summary>
        public abstract string Username { get; }
        /// <summary>
        /// Sentry file to be hashed and be written to during steam callbacks.
        /// </summary>
        public abstract FileInfo Sentry { get; }
        /// <summary>
        /// Key used for passwordless login. Should be backed by some persistent storage, as steam server might send updated key.
        /// </summary>
        public abstract string LoginKey { get; set; }

        /// <summary>
        /// Handles confirming the new login key with steam.
        /// </summary>
        /// <param name="user"><see cref="SteamUser"/> handler for confirming new login key to servers.</param>
        /// <param name="obj">Callback containing new login key.</param>
        public void HandleLoginKey(SteamUser user, SteamUser.LoginKeyCallback obj)
        {
            LoginKey = obj.LoginKey;
            user.AcceptNewLoginKey(obj);
        }
        /// <summary>
        /// Handles updating sentry data and storing it in provided <see cref="Sentry"/> file.
        /// </summary>
        /// <param name="user"><see cref="SteamUser"/> handler for confirming sentry file data to servers.</param>
        /// <param name="obj">Callback containing data that needs to be written to the sentry file.</param>
        public void HandleMachineAuthCallback(SteamUser user, SteamUser.UpdateMachineAuthCallback obj)
        {
            int fileSize;
            byte[] sentryHash;
            using (var fs = Sentry.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(obj.Offset, SeekOrigin.Begin);
                fs.Write(obj.Data, 0, obj.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = SHA1.Create())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }

            // inform the steam servers that we're accepting this sentry file
            user.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = obj.JobID,

                FileName = obj.FileName,

                BytesWritten = obj.BytesToWrite,
                FileSize = fileSize,
                Offset = obj.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = obj.OneTimePassword,

                SentryFileHash = sentryHash,
            });
        }
        /// <summary>
        /// Handles logging in process.
        /// </summary>
        /// <param name="user"><see cref="SteamUser"/> handler for sending login data.</param>
        public void HandleConnectedCallback(SteamUser user)
        {
            byte[]? hash = null;
            if (Sentry.Exists)
            {
                using (var sha = SHA1.Create())
                {
                    using (var fs = Sentry.OpenRead())
                    {
                        hash = sha.ComputeHash(fs);
                    }
                }
            }
            user.LogOn(new SteamUser.LogOnDetails
            {
                Username = Username,
                LoginKey = LoginKey,
                ShouldRememberPassword = true,
                SentryFileHash = hash
            });
        }
    }
}

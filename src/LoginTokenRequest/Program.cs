using CommandLine;
using SteamKit2;
using System;

namespace LoginTokenRequest
{
    class Program
    {
        private static SteamClient client;
        private static SteamUser user;
        private static CallbackManager manager;
        private static ParserResult<Options> options;

        private static bool keepRunning = true;
        private static string authCode = null, twoFactorAuth = null;

        static void Main(string[] args)
        {
            options = Parser.Default.ParseArguments<Options>(args);

            client = new SteamClient();
            manager = new CallbackManager(client);
            user = client.GetHandler<SteamUser>();

            using var connected = manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            using var disconnected = manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            using var loginKey = manager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
            using var loggedOn = manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            using var loggedOff = manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

            client.Connect();

            while (keepRunning || client.IsConnected)
            {
                manager.RunWaitCallbacks();
            }
        }

        private static void OnLoginKey(SteamUser.LoginKeyCallback obj)
        {
            user.AcceptNewLoginKey(obj);
            Console.WriteLine($"Your login key: {obj.LoginKey}");
            user.LogOff();
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback obj)
        {
            bool isSteamGuard = obj.Result == EResult.AccountLogonDenied;
            bool is2FA = obj.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");

                if (is2FA)
                {
                    Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                    twoFactorAuth = Console.ReadLine();
                }
                else
                {
                    Console.Write("Please enter the auth code sent to the email at {0}: ", obj.EmailDomain);
                    authCode = Console.ReadLine();
                }

                return;
            }

            if (obj.Result != EResult.OK)
            {
                Console.WriteLine("Unable to logon to Steam: {0} / {1}", obj.Result, obj.ExtendedResult);
                keepRunning = false;
                return;
            }

            Console.WriteLine("Logged on!");
        }

        private static void OnConnected(SteamClient.ConnectedCallback obj)
        {
            options.WithParsed(opt =>
            {
                user.LogOn(new SteamUser.LogOnDetails()
                {
                    Username = opt.Username,
                    Password = opt.Password,
                    LoginID = opt.LoginId,
                    ShouldRememberPassword = true,
                    TwoFactorCode = twoFactorAuth,
                    AuthCode = authCode
                });
            });
        }

        private static void OnDisconnected(SteamClient.DisconnectedCallback obj)
        {
            if (obj.UserInitiated)
            {
                keepRunning = false;
                return;
            }

            client.Connect();
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback obj) => Console.WriteLine($"Logged off of Steam with {obj.Result}");
    }
}
using Microsoft.Extensions.Logging;
using SteamKit2;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SteamUserAuthToken
{
    /// <summary>
    /// Handles logging in to steam servers with provided <see cref="SteamCredentials"/>.
    /// </summary>
    /// <remarks>
    /// Creates long running background task for handling steam callbacks. 
    /// Preferable way to use it would be a single instance, since process of logging in takes a while.
    /// </remarks>
    public sealed class SteamHandling : IDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private readonly IDisposable _onConnected;
        private readonly IDisposable _onDisconnected;
        private readonly IDisposable _onLoginKey;
        private readonly IDisposable _onLoggedOn;
        private readonly IDisposable _onMachineAuth;

        private readonly SteamCredentials _credentials;
        private readonly ILogger<SteamHandling>? _logger;

        private readonly Task callbacksTask;
        private readonly SteamUser _user;
        private readonly SteamClient _client;
        private readonly CallbackManager _manager;
        private readonly SteamAuthTicket _authTicket;

        /// <summary>
        /// Sets up steam connection and sets logger to use for debug information.
        /// </summary>
        /// <param name="steamCredentials">Credentials to use for login.</param>
        /// <param name="logger">Logger instance.</param>
        public SteamHandling(SteamCredentials steamCredentials, ILogger<SteamHandling> logger) : this(steamCredentials)
        {
            _logger = logger;
        }
        /// <summary>
        /// Sets up steam connection.
        /// </summary>
        /// <param name="steamCredentials">Credentials to use for login.</param>
        public SteamHandling(SteamCredentials steamCredentials)
        {
            _credentials = steamCredentials;

            _client = new SteamClient();
            _manager = new CallbackManager(_client);
            _authTicket = new SteamAuthTicket(_client, _manager);
            _user = _client.GetHandler<SteamUser>();

            _onConnected = _manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            _onDisconnected = _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            _onLoginKey = _manager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
            _onLoggedOn = _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            _onMachineAuth = _manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuthDetails);

            _client.Connect();

            callbacksTask = Task.Factory.StartNew(() => 
            {
                while (true)
                {
                    _manager.RunWaitCallbacks();
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// Performs session ticket generation and validation for specified <paramref name="gameID"/>.
        /// </summary>
        /// <param name="gameID">Game to generate ticket for.</param>
        /// <returns>Bytes representing the session ticket, or empty array if You don't own the game.</returns>
        public Task<ReadOnlyMemory<byte>> GetAuthSessionTicket(GameID gameID) => _authTicket.GetAuthSessionTicket(gameID);

        /// <summary>
        /// Cleans up allocated resources.
        /// </summary>
        public void Dispose()
        {
            _user.LogOff();
            callbacksTask.Wait();
            _onLoginKey.Dispose();
            _onLoggedOn.Dispose();
            _onMachineAuth.Dispose();
            _onConnected.Dispose();
            _onDisconnected.Dispose();
            _authTicket.Dispose();
            cts.Dispose();
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback obj)
        {
            if (obj.Result != EResult.OK)
                _logger?.LogError($"Invalid login ({_credentials.Username}): {obj.Result} / {obj.ExtendedResult}");
        }
        private void OnDisconnected(SteamClient.DisconnectedCallback obj)
        {
            if(obj.UserInitiated)
                _logger?.LogInformation($"Disconnect requested by user.");

            cts.Cancel();
        }
        private void OnLoginKey(SteamUser.LoginKeyCallback obj) => _credentials.HandleLoginKey(_user, obj);
        private void OnConnected(SteamClient.ConnectedCallback obj) => _credentials.HandleConnectedCallback(_user);
        private void OnMachineAuthDetails(SteamUser.UpdateMachineAuthCallback obj) => _credentials.HandleMachineAuthCallback(_user, obj);
    }
}

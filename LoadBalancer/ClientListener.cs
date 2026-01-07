using System.Net;
using System.Net.Sockets;

namespace LoadBalancer
{
    /// <summary>
    /// Provides a background service that listens for incoming TCP client connections and delegates them to the backend
    /// manager for processing.
    /// </summary>
    public class ClientListener : BackgroundService
    {
        private readonly ILogger<ClientListener> _logger;
        private readonly IBackendManager _backendManager;
        private readonly int _listenPort = 9000;

        public ClientListener(ILogger<ClientListener> logger, IConfiguration configuration, IBackendManager backendManager)
        {
            _logger = logger;
            _backendManager = backendManager;
            if (int.TryParse(configuration["ListenPort"], out int port) && port > 0)
            {
                _listenPort = port;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var listener = new TcpListener(IPAddress.Any, _listenPort);
            listener.Start();

            _logger.LogInformation($"L4 load balancer listening on port {_listenPort}");

            while (!stoppingToken.IsCancellationRequested)
            {
                var newclient = await listener.AcceptTcpClientAsync(stoppingToken);
                _backendManager.StartNewBackendConnection(new TcpClientWrapper(newclient));
            }
        }
    }
}
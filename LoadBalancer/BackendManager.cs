using LoadBalancer.BackendSelectors;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace LoadBalancer
{
    /// <summary>
    /// Defines functionality for managing backend connections.
    /// </summary>
    public interface IBackendManager
    {
        void StartNewBackendConnection(ITcpClient client);
    }

    /// <summary>
    /// Represents a network backend endpoint and its online status.
    /// </summary>
    public class Backend
    {
        // this field is volatile to ensure thread-safe access across multiple threads
        private volatile bool _online;
        public bool Online { get { return _online; } set { _online = value; } }
        public IPEndPoint IPEndPoint { get; }
        
        public Backend(IPEndPoint iPEndPoint, bool online)
        {
            IPEndPoint = iPEndPoint;
            Online = online;
        }
    }

    /// <summary>
    /// Provides management and coordination of backend servers for load balancing, including backend selection and
    /// connection handling.
    /// </summary>
    public class BackendManager : IBackendManager
    {
        private readonly Backend[] _backends;
        private readonly IBackendSelector? _backendSelector = null;
        private readonly ILogger<BackendManager> _logger;
        private readonly ITcpClientFactory _tcpClientFactory;

        public BackendManager(ILogger<BackendManager> logger, IConfiguration configuration, IBackendSelector backendSelector, ITcpClientFactory? tcpClientFactory = null) {

            _logger = logger;
            _backendSelector = backendSelector;
            _tcpClientFactory = tcpClientFactory ?? new DefaultTcpClientFactory();

            // Create the IP endpoints for the configured backends
            var backendstrings = configuration["Backends"]?.Split(',').ToArray() ?? Array.Empty<string>();

            _backends = new Backend[backendstrings.Length];
            try
            {
                if (!backendstrings.Any())
                    throw new Exception("No backends found");

                for (int i = 0; i < backendstrings.Length; i++)
                {
                    var backend = backendstrings[i];
                    var split = backend.Split(":");
                    if (split.Length != 2)
                        throw new Exception($"Backend string incorrectly formed ({backend})");

                    IPAddress? ipaddress;
                    if (!IPAddress.TryParse(split[0], out ipaddress) || ipaddress == null)
                        throw new Exception($"Could not parse ipaddress ({split[0]})");

                    int port;
                    if (!int.TryParse(split[1], out port) || port <= 0)
                        throw new Exception($"Could not parse port ({split[1]})");

                    _backends[i] = new Backend(new IPEndPoint(ipaddress, port), true);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Error when creating the IP endpoints for the configured backends: {ex}", ex);
                return;
            }

            logger.LogInformation($"L4 load balancer using {_backendSelector.GetType().Name}. Backends: {string.Join(", ", _backends.Select(b => b.IPEndPoint))}");
        }


        public void StartNewBackendConnection(ITcpClient connectingClient)
        {
            // start a seperate thread for this so this method doesn't wait (or capture exceptions, but those are logged)
            Task.Run(async () => {
                try
                {
                    var backendClient = await ConnectToNextBackendAsync(connectingClient);
                    if (backendClient != null)
                    {
                        var connectingClientEndPoint = connectingClient.RemoteEndPoint as IPEndPoint;
                        var backendClientEndPoint = backendClient.RemoteEndPoint as IPEndPoint;
                        _logger.LogInformation($"Forwarding connection: {connectingClientEndPoint} <-> {backendClientEndPoint}");
                        await DataTransfer.DoDataTransferAsync(_logger, connectingClient, backendClient);
                    }
                }
                finally
                {
                    connectingClient.Dispose();
                }
            });
        }

        /// <summary>
        /// Attempts to establish a connection to the next available backend server for the specified client.
        /// </summary>
        /// <param name="connectingClient">The client initiating the connection request. Must be a valid, connected TcpClient instance.</param>
        /// <returns>A TcpClient connected to the selected backend server if successful; otherwise, null if no backend is
        /// available or an error occurs during selection or connection.</returns>
        protected async Task<ITcpClient?> ConnectToNextBackendAsync(ITcpClient connectingClient)
        {
            if (_backendSelector == null)
                return null; // should never happen

            ITcpClient backendClient = _tcpClientFactory.Create();

            // we need to find the next available backend
            int selectedBackendIndex;
            int attempt = 0;

            while (!backendClient.Connected)
            {
                // first try to get the next backend service to use
                try
                {
                    attempt++;
                    if (attempt > _backends.Length)
                        throw new Exception("Exhausted all backend options");

                    selectedBackendIndex = _backendSelector.Next(_backends, connectingClient.RemoteEndPoint as IPEndPoint);
                    if (selectedBackendIndex < 0 || selectedBackendIndex >= _backends.Length)
                        throw new Exception("Returned index not in range");
                }
                catch (Exception ex)
                {
                    // this means the selector is not working or we have run out of backend options and we should mark this as a critical event (and alert on it)
                    _logger.LogCritical($"Error selecting backend: {ex.Message}");
                    backendClient.Dispose();
                    return null;
                }

                var selectedBackend = _backends[selectedBackendIndex];
                if (selectedBackend.Online)
                {
                    // then open up a connection with it                   
                    try
                    {
                        await backendClient.ConnectAsync(selectedBackend.IPEndPoint.Address, selectedBackend.IPEndPoint.Port);
                    }
                    catch (Exception ex)
                    {
                        selectedBackend.Online = false;
                        _logger.LogWarning($"Error conecting to backend {selectedBackend.IPEndPoint} so it is marked as offline: {ex.Message}");
                    }
                }
            }

            return backendClient;
        }
    }
}

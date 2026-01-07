using System.Net;

namespace LoadBalancer.BackendSelectors
{
    /// <summary>
    /// Selects backends in a round-robin sequence for load balancing purposes.
    /// </summary>
    public class RoundRobinSelector : IBackendSelector
    {
        private int _backendIndex = -1; // initial

        public int Next(Backend[] backends, IPEndPoint? connectingClientEndpoint)
        {
            _backendIndex = (_backendIndex + 1) % backends.Length;
            return _backendIndex;
        }
    }
}

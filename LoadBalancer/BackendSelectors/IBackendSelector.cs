using System.Net;

namespace LoadBalancer.BackendSelectors
{
    /// <summary>
    /// Defines a strategy for selecting a backend server to handle a new client connection attempt.
    /// </summary>
    public interface IBackendSelector
    {
        /// <summary>
        /// Selects the index of the backend to use for a new connection attempt.
        /// </summary>
        /// <remarks>The selection strategy may use information from <paramref name="connectingClientEndpoint"/> to influence backend choice.
        /// The method does not modify the input array.</remarks>
        /// <param name="backends">An array of available backend servers to choose from. Cannot be null or empty.</param>
        /// <param name="connectingClientEndpoint">The network endpoint of the client attempting to connect, or null if the client endpoint is not known.</param>
        /// <returns>The zero-based index of the selected backend in the <paramref name="backends"/> array.</returns>
        int Next(Backend[] backends, IPEndPoint? connectingClientEndpoint);
    }
}

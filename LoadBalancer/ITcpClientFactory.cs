using System.Net;
using System.Net.Sockets;

namespace LoadBalancer
{
    /// <summary>
    /// Factory for creating <see cref="ITcpClient"/> instances. Allows tests to provide a testable implementation.
    /// </summary>
    public interface ITcpClientFactory
    {
        ITcpClient Create();
    }

    public interface ITcpClient : IDisposable
    {
        Task ConnectAsync(IPAddress address, int port);
        Stream GetStream();
        void Close();
        bool Connected { get; }
        EndPoint? RemoteEndPoint { get; }
    }

    public class DefaultTcpClientFactory : ITcpClientFactory
    {
        public ITcpClient Create() => new TcpClientWrapper();
    }

    public class TcpClientWrapper : ITcpClient
    {
        private readonly TcpClient _tcpClient;

        public bool Connected => _tcpClient.Connected;

        public EndPoint? RemoteEndPoint => _tcpClient.Client.RemoteEndPoint;

        public TcpClientWrapper()
        {
            _tcpClient = new TcpClient();
        }

        public TcpClientWrapper(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
        }

        public async Task ConnectAsync(IPAddress address, int port)
        {
            await _tcpClient.ConnectAsync(address, port);
        }

        public Stream GetStream()
        {
            return _tcpClient.GetStream();
        }

        public void Close()
        {
            _tcpClient.Close();
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }
}

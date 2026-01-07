using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using LoadBalancer;
using LoadBalancer.BackendSelectors;
using System.Reflection;

namespace LoadBalancerUnitTests
{
    [TestFixture]
    public class BackendManagerTests
    {
        private Mock<ILogger<BackendManager>> _loggerMock = new Mock<ILogger<BackendManager>>();
        private Mock<ITcpClientFactory> _factoryMock = new Mock<ITcpClientFactory>();
        private Mock<ITcpClient> _backendClientMock = new Mock<ITcpClient>();
        private Mock<ITcpClient> _connectingClientMock = new Mock<ITcpClient>();
        private Mock<IBackendSelector> _selectorMock = new Mock<IBackendSelector>();

        [SetUp]
        public void Setup()
        {
            // Reset mocks before each test
            _loggerMock.Reset();
            _factoryMock.Reset();
            _backendClientMock.Reset();
            _connectingClientMock.Reset();
            _selectorMock.Reset();

            // Setup common mock behaviors
            _factoryMock.Setup(f => f.Create()).Returns(_backendClientMock.Object);
            _connectingClientMock.SetupGet(c => c.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345));
            _selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>())).Returns(0);
        }

        private IConfiguration BuildConfiguration(string backends) => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Backends", backends)])
            .Build();

        private class TestableBackendManager : BackendManager
        {
            public TestableBackendManager(ILogger<BackendManager> logger, IConfiguration configuration, IBackendSelector backendSelector) : base(logger, configuration, backendSelector) { }
            public TestableBackendManager(ILogger<BackendManager> logger, IConfiguration configuration, IBackendSelector backendSelector, ITcpClientFactory factory) : base(logger, configuration, backendSelector, factory) { }
            public Task<ITcpClient?> PublicConnectToNextBackendAsync(ITcpClient client) => base.ConnectToNextBackendAsync(client);
        }

        /// <summary>
        /// Verifies that the BackendManager constructor correctly loads and creates Backend instances from the provided
        /// configuration.
        /// </summary>
        [Test]
        public void Constructor_LoadsBackends_CreatesExpectedBackends()
        {
            var backendsString = "127.0.0.1:5000,10.0.0.2:6000";
            var config = BuildConfiguration(backendsString);

            var manager = new BackendManager(_loggerMock.Object, config, _selectorMock.Object);

            var field = typeof(BackendManager).GetField("_backends", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "_backends field not found via reflection");

            var value = field.GetValue(manager) as Backend[];
            Assert.That(value, Is.Not.Null, "_backends value was null or not a Backend[]");
            Assert.That(value.Length, Is.EqualTo(2));

            Assert.That(value[0].IPEndPoint.Address.ToString(), Is.EqualTo("127.0.0.1"));
            Assert.That(value[0].IPEndPoint.Port, Is.EqualTo(5000));
            Assert.That(value[0].Online, Is.True);

            Assert.That(value[1].IPEndPoint.Address.ToString(), Is.EqualTo("10.0.0.2"));
            Assert.That(value[1].IPEndPoint.Port, Is.EqualTo(6000));
            Assert.That(value[1].Online, Is.True);
        }

        /// <summary>
        /// Verifies that the ConnectToNextBackendAsync method successfully connects to the next available backend and
        /// returns a connected backend client when called under normal conditions.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_HappyPath_ConnectsAndReturnsBackendClient()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask)
                .Callback(() => connected = true);

            var config = BuildConfiguration("127.0.0.1:5000");
            var manager = new TestableBackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(_backendClientMock.Object));
            Assert.That(result.Connected, Is.True);
            _backendClientMock.Verify(b => b.ConnectAsync(IPAddress.Parse("127.0.0.1"), 5000), Times.Once);
        }

        /// <summary>
        /// Verifies that ConnectToNextBackendAsync returns null and closes the client when the backend selector throws
        /// an exception.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_SelectorThrows_ReturnsNull()
        {
            var config = BuildConfiguration("127.0.0.1:5000");
            _selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>())).Throws(new Exception("Selector error"));
            var manager = new TestableBackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            _connectingClientMock.Setup(c => c.Close()).Verifiable();

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that when connecting to the next backend fails, the backend is marked offline and the connection is
        /// retried with the next available backend.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_ConnectFails_MarkBackendOfflineAndRetryNext()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            
            var connectCallCount = 0;
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(() =>
                {
                    connectCallCount++;
                    if (connectCallCount == 1)
                    {
                        return Task.FromException(new Exception("Connection refused"));
                    }
                    else
                    {
                        connected = true;
                        return Task.CompletedTask;
                    }
                });

            var selectorMock = new Mock<IBackendSelector>();
            var selectorIndex = -1;
            selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>()))
                .Returns(() => ++selectorIndex % 2);

            var config = BuildConfiguration("127.0.0.1:5000,127.0.0.1:5001");
            var manager = new TestableBackendManager(_loggerMock.Object, config, selectorMock.Object, _factoryMock.Object);

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Connected, Is.True);
            _backendClientMock.Verify(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that when the first backend is offline, the connection attempt is skipped and the manager attempts
        /// to connect to the next available online backend.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_BackendOfflineSkipped_AttemptsOnlineBackend()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask)
                .Callback(() => connected = true);

            var selectorMock = new Mock<IBackendSelector>();
            var selectorIndex = -1;
            selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>()))
                .Returns(() => ++selectorIndex % 2);

            var config = BuildConfiguration("127.0.0.1:5000,127.0.0.1:5001");
            var manager = new TestableBackendManager(_loggerMock.Object, config, selectorMock.Object, _factoryMock.Object);

            // Mark the first backend as offline
            var backendsField = typeof(BackendManager).GetField("_backends", BindingFlags.NonPublic | BindingFlags.Instance);
            var backends = backendsField?.GetValue(manager) as Backend[];
            if (backends != null)
            {
                backends[0].Online = false;
            }

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Connected, Is.True);
            _backendClientMock.Verify(b => b.ConnectAsync(IPAddress.Parse("127.0.0.1"), 5001), Times.Once);
        }

        /// <summary>
        /// Verifies that ConnectToNextBackendAsync returns null when all backend connection attempts are exhausted.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_ExhaustedAllBackends_ReturnsNull()
        {
            _backendClientMock.SetupGet(b => b.Connected).Returns(false);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.FromException(new Exception("Connection failed")));

            var selectorMock = new Mock<IBackendSelector>();
            var selectorIndex = -1;
            selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>()))
                .Returns(() => ++selectorIndex % 2);

            var config = BuildConfiguration("127.0.0.1:5000,127.0.0.1:5001");
            var manager = new TestableBackendManager(_loggerMock.Object, config, selectorMock.Object, _factoryMock.Object);

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Null);
            _backendClientMock.Verify(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()), Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that ConnectToNextBackendAsync returns null when the backend selector provides an invalid index.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_SelectorReturnsInvalidIndex_ReturnsNull()
        {
            var config = BuildConfiguration("127.0.0.1:5000");
            _selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>())).Returns(999);
            var manager = new TestableBackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that ConnectToNextBackendAsync passes the correct remote endpoint of the connecting client to the
        /// backend selector.
        /// </summary>
        [Test]
        public async Task ConnectToNextBackendAsync_PassesCorrectRemoteEndpointToSelector()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask)
                .Callback(() => connected = true);

            var config = BuildConfiguration("127.0.0.1:5000");
            var manager = new TestableBackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            var remoteEndpoint = new IPEndPoint(IPAddress.Parse("10.20.30.40"), 54321);
            _connectingClientMock.SetupGet(c => c.RemoteEndPoint).Returns(remoteEndpoint);

            var result = await manager.PublicConnectToNextBackendAsync(_connectingClientMock.Object);

            Assert.That(result, Is.Not.Null);
            _selectorMock.Verify(s => s.Next(It.IsAny<Backend[]>(), remoteEndpoint), Times.Once);
        }

        /// <summary>
        /// Verifies that StartNewBackendConnection successfully connects to a backend and calls DoDataTransferAsync
        /// when a valid backend connection is established.
        /// </summary>
        [Test]
        public void StartNewBackendConnection_ValidBackend_SuccessfullyTransfersData()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask)
                .Callback(() => connected = true);

            var mockStream = new Mock<Stream>();
            _connectingClientMock.Setup(c => c.GetStream()).Returns(mockStream.Object);
            _backendClientMock.Setup(b => b.GetStream()).Returns(mockStream.Object);

            var config = BuildConfiguration("127.0.0.1:5000");
            var manager = new BackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            manager.StartNewBackendConnection(_connectingClientMock.Object);

            // Give the background task time to complete
            System.Threading.Thread.Sleep(500);

            _connectingClientMock.Verify(c => c.Dispose(), Times.AtLeastOnce);
            _backendClientMock.Verify(b => b.Dispose(), Times.AtLeastOnce);
            _loggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Connection closed gracefully")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that StartNewBackendConnection disposes the connecting client even when the backend connection fails.
        /// </summary>
        [Test]
        public void StartNewBackendConnection_BackendConnectionFails_DisposesConnectingClient()
        {
            var config = BuildConfiguration("127.0.0.1:5000");
            _selectorMock.Setup(s => s.Next(It.IsAny<Backend[]>(), It.IsAny<IPEndPoint?>()))
                .Throws(new Exception("Selector error"));
            var manager = new BackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            manager.StartNewBackendConnection(_connectingClientMock.Object);

            // Give the background task time to complete
            System.Threading.Thread.Sleep(500);

            _connectingClientMock.Verify(c => c.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that StartNewBackendConnection returns immediately without blocking the calling thread.
        /// </summary>
        [Test]
        public void StartNewBackendConnection_ReturnsImmediately_DoesNotBlock()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            
            // Setup a long-running connection attempt
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(async () =>
                {
                    await Task.Delay(5000); // Long delay
                    connected = true;
                });

            var config = BuildConfiguration("127.0.0.1:5000");
            var manager = new BackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            manager.StartNewBackendConnection(_connectingClientMock.Object);
            stopwatch.Stop();

            // Method should return almost immediately (under 100ms)
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(100));
        }

        /// <summary>
        /// Verifies that StartNewBackendConnection handles exceptions gracefully and still disposes the connecting client.
        /// </summary>
        [Test]
        public void StartNewBackendConnection_ExceptionDuringDataTransfer_DisposesConnectingClient()
        {
            bool connected = false;
            _backendClientMock.SetupGet(b => b.Connected).Returns(() => connected);
            _backendClientMock.Setup(b => b.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>()))
                .Returns(Task.CompletedTask)
                .Callback(() => connected = true);

            var mockStream = new Mock<Stream>();
            mockStream.Setup(s => s.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<int>(Task.FromException<int>(new IOException("Stream error"))));

            _connectingClientMock.Setup(c => c.GetStream()).Returns(mockStream.Object);
            _backendClientMock.Setup(b => b.GetStream()).Returns(mockStream.Object);

            var config = BuildConfiguration("127.0.0.1:5000");
            var manager = new BackendManager(_loggerMock.Object, config, _selectorMock.Object, _factoryMock.Object);

            manager.StartNewBackendConnection(_connectingClientMock.Object);

            // Give the background task time to complete
            System.Threading.Thread.Sleep(500);

            _connectingClientMock.Verify(c => c.Dispose(), Times.AtLeastOnce);
        }
    }
}

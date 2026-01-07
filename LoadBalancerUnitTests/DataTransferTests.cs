using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using LoadBalancer;

namespace LoadBalancerUnitTests
{
    [TestFixture]
    public class DataTransferTests
    {
        private IConfiguration BuildConfiguration(string backends) => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Backends", backends)])
            .Build();

        /// <summary>
        /// Verifies that DoDataTransferAsync successfully transfers data from client to backend and closes the backend client
        /// when the client stream ends.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_ClientToBackendTransfer_TransfersDataAndClosesBackendClient()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var testData = Encoding.UTF8.GetBytes("Hello from client");
            clientStream.Write(testData, 0, testData.Length);
            clientStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);
            backendMock.Setup(b => b.Dispose()).Verifiable();

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            backendStream.Position = 0;
            var receivedData = new StreamReader(backendStream).ReadToEnd();
            Assert.That(receivedData, Is.EqualTo("Hello from client"));
            backendMock.Verify(b => b.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync successfully transfers data from backend to client and closes the client
        /// when the backend stream ends.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_BackendToClientTransfer_TransfersDataAndClosesClient()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var testData = Encoding.UTF8.GetBytes("Hello from backend");
            backendStream.Write(testData, 0, testData.Length);
            backendStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);
            clientMock.Setup(c => c.Close()).Verifiable();
            backendMock.Setup(b => b.Close()).Verifiable();

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            clientStream.Position = 0;
            var receivedData = new StreamReader(clientStream).ReadToEnd();
            Assert.That(receivedData, Is.EqualTo("Hello from backend"));
            clientMock.Verify(c => c.Dispose(), Times.Once);
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync handles exceptions during data transfer and closes both client and backend.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_ExceptionDuringTransfer_ClosesBothClientsAndLogsError()
        {
            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Throws(new IOException("Stream error"));
            backendMock.Setup(b => b.GetStream()).Throws(new IOException("Stream error"));
            clientMock.Setup(c => c.Dispose()).Verifiable();
            backendMock.Setup(b => b.Dispose()).Verifiable();

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            clientMock.Verify(c => c.Dispose(), Times.Once);
            backendMock.Verify(b => b.Dispose(), Times.Once);
            loggerMock.Verify(
                l => l.Log(
                    It.Is<LogLevel>(ll => ll == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync correctly relays data from client stream to backend stream with the expected buffer size.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_RelaysDataCorrectly_ClientToBackend()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var testData = Encoding.UTF8.GetBytes("Test data from client");
            clientStream.Write(testData, 0, testData.Length);
            clientStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            backendStream.Position = 0;
            var relayedData = new StreamReader(backendStream).ReadToEnd();
            Assert.That(relayedData, Is.EqualTo("Test data from client"));
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync correctly relays large data packets from client to backend.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_RelaysLargeData_ClientToBackendReceivesCompleteData()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var largeData = new byte[20000];
            for (int i = 0; i < largeData.Length; i++)
            {
                largeData[i] = (byte)(i % 256);
            }
            clientStream.Write(largeData, 0, largeData.Length);
            clientStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            backendStream.Position = 0;
            var relayedData = backendStream.ToArray();
            Assert.That(relayedData, Is.EqualTo(largeData), "Large data should be completely relayed to backend");
            Assert.That(relayedData.Length, Is.EqualTo(20000));
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync correctly relays data from backend to client with exact byte-for-byte accuracy.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_RelaysDataCorrectly_BackendToClientReceivesExactData()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var testData = Encoding.UTF8.GetBytes("Backend response data");
            backendStream.Write(testData, 0, testData.Length);
            backendStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            clientStream.Position = 0;
            var relayedData = clientStream.ToArray();
            Assert.That(relayedData, Is.EqualTo(testData), "Data should be relayed exactly to client");
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync preserves binary data integrity when relaying between client and backend.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_PreservesBinaryData_IntegrityMaintainedBothDirections()
        {
            var clientStream = new MemoryStream();
            var backendStream = new MemoryStream();

            var binaryData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD, 0xFC, 0x80, 0x7F };
            clientStream.Write(binaryData, 0, binaryData.Length);
            clientStream.Position = 0;

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            backendStream.Position = 0;
            var receivedBinaryData = backendStream.ToArray();
            Assert.That(receivedBinaryData, Is.EqualTo(binaryData), "Binary data integrity must be maintained");
            for (int i = 0; i < binaryData.Length; i++)
            {
                Assert.That(receivedBinaryData[i], Is.EqualTo(binaryData[i]), $"Byte at index {i} should match exactly");
            }
        }

        /// <summary>
        /// Verifies that DoDataTransferAsync throws "Backend disconnected" exception and logs an error when the backend disconnects first.
        /// </summary>
        [Test]
        public async Task DoDataTransferAsync_BackendDisconnectsFirst_ThrowsBackendDisconnectedExceptionAndLogsError()
        {
            var backendStream = new MemoryStream();
            var clientStream = new BlockingStream();

            var clientMock = new Mock<ITcpClient>();
            var backendMock = new Mock<ITcpClient>();

            clientMock.Setup(c => c.GetStream()).Returns(clientStream);
            clientMock.Setup(c => c.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000));
            backendMock.Setup(b => b.GetStream()).Returns(backendStream);
            backendMock.Setup(b => b.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Parse("192.168.1.200"), 8080));
            clientMock.Setup(c => c.Dispose()).Verifiable();
            backendMock.Setup(b => b.Dispose()).Verifiable();

            var loggerMock = new Mock<ILogger>();

            await DataTransfer.DoDataTransferAsync(loggerMock.Object, clientMock.Object, backendMock.Object);

            clientMock.Verify(c => c.Dispose(), Times.Once);
            backendMock.Verify(b => b.Dispose(), Times.Once);
            loggerMock.Verify(
                l => l.Log(
                    It.Is<LogLevel>(ll => ll == LogLevel.Error),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Backend disconnected")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        private class BlockingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Task.Delay(Timeout.Infinite).Wait();
                return 0;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { }
            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await Task.CompletedTask;
        }
    }
}

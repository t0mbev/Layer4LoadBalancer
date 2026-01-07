using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using LoadBalancer;

namespace LoadBalancerUnitTests
{
    [TestFixture]
    public class ClientListenerTests
    {
        [Test]
        public async Task ExecuteAsync_AcceptsConnection_InvokesBackendManager()
        {
            // Reserve a free ephemeral port
            var temp = new TcpListener(IPAddress.Loopback, 0);
            temp.Start();
            var port = ((IPEndPoint)temp.LocalEndpoint).Port;
            temp.Stop();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection([ new KeyValuePair<string, string?>("ListenPort", port.ToString()) ])
                .Build();

            var loggerMock = new Mock<ILogger<ClientListener>>();
            var backendManagerMock = new Mock<IBackendManager>();

            var tcs = new TaskCompletionSource<ITcpClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
            backendManagerMock.Setup(b => b.StartNewBackendConnection(It.IsAny<ITcpClient>()))
                       .Callback<ITcpClient>(c => tcs.TrySetResult(c));

            var listener = new TestClientListener(loggerMock.Object, config, backendManagerMock.Object);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var executeTask = listener.PublicExecuteAsync(cts.Token);

            // Give the listener a short moment to start
            await Task.Delay(250);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));

            Assert.That(tcs.Task.IsCompleted, Is.True, "Backend manager was not invoked");

            // Cancel and wait for background task to finish
            cts.Cancel();
            try { await executeTask; } catch (OperationCanceledException) { }
        }

        // test helper to expose protected ExecuteAsync
        private class TestClientListener : ClientListener
        {
            public TestClientListener(ILogger<ClientListener> logger, IConfiguration config, IBackendManager backendManager)
                : base(logger, config, backendManager) { }

            public Task PublicExecuteAsync(CancellationToken token) => base.ExecuteAsync(token);
        }
    }
}

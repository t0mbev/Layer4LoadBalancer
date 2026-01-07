namespace LoadBalancer
{
    /// <summary>
    /// Handles asynchronous data transfer between client and backend TCP connections.
    /// </summary>
    public class DataTransfer
    {
        /// <summary>
        /// Transfers data asynchronously between the client and the backend service over TCP connections.
        /// </summary>
        /// <param name="logger">The logger used to record errors that occur during the data transfer process.</param>
        /// <param name="client">The TCP client representing the source or destination of the data transfer.</param>
        /// <param name="backendClient">The TCP client representing the backend service to which data is relayed.</param>
        /// <returns>A task that represents the asynchronous data transfer operation.</returns>
        public async static Task DoDataTransferAsync(ILogger logger, ITcpClient client, ITcpClient backendClient)
        {
            // now pass data to/from the chosen backend service from/to the client
            try
            {
                var cs = client.GetStream();
                var bs = backendClient.GetStream();
                var clientDisconnected = false;

                // Pipe client -> backend
                var t1 = Task.Run(async () =>
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await cs.ReadAsync(buffer)) > 0)
                        await bs.WriteAsync(buffer.AsMemory(0, read));
                    clientDisconnected = true;
                });

                // Pipe backend -> client
                var t2 = Task.Run(async () =>
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await bs.ReadAsync(buffer)) > 0)
                        await cs.WriteAsync(buffer.AsMemory(0, read));
                });

                await Task.WhenAny(t1, t2);

                if (!clientDisconnected)
                    throw new Exception("Backend disconnected");

                logger.LogInformation($"Connection closed gracefully between client ({client.RemoteEndPoint}) and backend ({backendClient.RemoteEndPoint})");
            }
            catch (Exception ex)
            {
                logger.LogError($"Connection closed due to error between client ({client.RemoteEndPoint}) and backend ({backendClient.RemoteEndPoint}): {ex.Message}");
            }
            finally
            {
                client.Dispose();
                backendClient.Dispose();
            }
        }
    }
}

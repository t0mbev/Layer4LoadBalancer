using System.Net;
using System.Net.Sockets;
using System.Text;

var port = 5001;
if (args.Length > 0 && int.TryParse(args[0], out var value))
    port = value;

var listener = new TcpListener(IPAddress.Any, port); // change port per instance
listener.Start();
Console.WriteLine($"Echo server on port {port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        using var stream = client.GetStream();
        var buffer = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            var msg = Encoding.UTF8.GetString(buffer, 0, read);
            var response = Encoding.UTF8.GetBytes($"[{port}] {msg}");
            await stream.WriteAsync(response);
        }
    });
}
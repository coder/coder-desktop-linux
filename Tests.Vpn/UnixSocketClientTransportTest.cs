using System.Net.Sockets;
using System.Runtime.Versioning;
using Coder.Desktop.Vpn;

namespace Coder.Desktop.Tests.Vpn;

[TestFixture]
[Platform("Linux", Reason = "UnixSocketClientTransport is Linux-only")]
[SupportedOSPlatform("linux")]
public class UnixSocketClientTransportTest
{
    [Test(Description = "ConnectAsync waits until the service socket exists")]
    [CancelAfter(30_000)]
    public async Task ConnectAsync_WaitsForSocketToAppear(CancellationToken ct)
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"coder-desktop-test-{Guid.NewGuid():N}.sock");
        Socket? listener = null;

        try
        {
            var transport = new UnixSocketClientTransport(socketPath);
            var connectTask = transport.ConnectAsync(ct);

            await Task.Delay(100, ct);
            Assert.That(connectTask.IsCompleted, Is.False);

            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            using var acceptedSocket = await listener.AcceptAsync(ct);
            await using var clientStream = await connectTask;
            Assert.That(clientStream.CanRead, Is.True);
        }
        finally
        {
            listener?.Dispose();
            try { File.Delete(socketPath); } catch { /* best effort */ }
        }
    }
}

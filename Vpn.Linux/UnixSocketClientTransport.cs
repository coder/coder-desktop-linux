using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Coder.Desktop.Vpn;

[SupportedOSPlatform("linux")]
public class UnixSocketClientTransport : IRpcClientTransport
{
    private readonly string _socketPath;

    public UnixSocketClientTransport(string socketPath = "/run/coder-desktop/vpn.sock")
    {
        var envSocketPath = Environment.GetEnvironmentVariable("CODER_DESKTOP_RPC_SOCKET_PATH");
        _socketPath = string.IsNullOrWhiteSpace(envSocketPath) ? socketPath : envSocketPath;
    }

    public async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        var retryDelay = TimeSpan.FromMilliseconds(100);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) when (IsSocketUnavailable(ex) && !ct.IsCancellationRequested)
            {
                socket.Dispose();
                await Task.Delay(retryDelay, ct);
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 1000));
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }

    private static bool IsSocketUnavailable(SocketException ex)
    {
        // Keep Linux startup behavior aligned with Windows named-pipe connect: wait for the service socket.
        return ex.SocketErrorCode is SocketError.AddressNotAvailable or SocketError.ConnectionRefused;
    }
}

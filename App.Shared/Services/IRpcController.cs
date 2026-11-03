using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

public class RpcOperationException : Exception
{
    public RpcOperationException(string message, Exception innerException) : base(message, innerException) { }
    public RpcOperationException(string message) : base(message) { }
}

public class VpnLifecycleException : Exception
{
    public VpnLifecycleException(string message, Exception innerException) : base(message, innerException) { }
    public VpnLifecycleException(string message) : base(message) { }
}

public interface IRpcController : IAsyncDisposable
{
    event EventHandler<RpcModel> StateChanged;
    RpcModel GetState();

    /// <summary>
    ///     Connects to the service and keeps the connection until the controller is disposed. After a failed attempt or
    ///     a dropped connection, the controller tries again after 1 second, doubling the wait up to 5 seconds. After
    ///     each connection, it starts the VPN if the VPN should run but the service reports it as stopped. Call this
    ///     after the credentials load, because the controller starts the VPN with the cached credentials.
    /// </summary>
    /// <param name="startVpn">Whether the VPN should run before the user starts or stops it</param>
    void Start(bool startVpn);

    /// <summary>
    ///     Ends the current wait between connection attempts, so the next attempt starts at once.
    /// </summary>
    void ReconnectNow();

    /// <summary>
    ///     Records that the VPN should run, then starts it. The VPN should run until StopVpn is called or the service
    ///     rejects a start.
    /// </summary>
    Task StartVpn(CancellationToken ct = default);

    /// <summary>
    ///     Records that the VPN should not run, then stops it.
    /// </summary>
    Task StopVpn(CancellationToken ct = default);
}

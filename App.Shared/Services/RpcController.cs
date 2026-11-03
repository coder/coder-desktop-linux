using Coder.Desktop.App.Models;
using Coder.Desktop.Vpn;
using Coder.Desktop.Vpn.Proto;
using Coder.Desktop.Vpn.Utilities;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.Services;

public class RpcController : IRpcController
{
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ILogger<RpcController> _logger;
    private readonly ICredentialManager _credentialManager;
    private readonly IRpcClientTransport _transport;

    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _reconnectNow = new(0, 1);
    private Task? _connectionLoop;

    private readonly RaiiSemaphoreSlim _operationLock = new(1, 1);
    private Speaker<ClientMessage, ServiceMessage>? _speaker;

    // Whether the VPN should run. The connection loop reads it after each
    // connection, so the VPN comes back after a service restart. Protected by
    // _operationLock.
    private bool _vpnWanted;

    private readonly RaiiSemaphoreSlim _stateLock = new(1, 1);
    private readonly RpcModel _state = new();

    public RpcController(ILogger<RpcController> logger, ICredentialManager credentialManager, IRpcClientTransport transport)
    {
        _logger = logger;
        _credentialManager = credentialManager;
        _transport = transport;
    }

    public event EventHandler<RpcModel>? StateChanged;

    public RpcModel GetState()
    {
        using var _ = _stateLock.Lock();
        return _state.Clone();
    }

    public void Start(bool startVpn)
    {
        if (_connectionLoop != null)
            throw new InvalidOperationException("RpcController is already started");
        _vpnWanted = startVpn;
        _connectionLoop = Task.Run(() => KeepConnected(_lifetimeCts.Token));
    }

    public void ReconnectNow()
    {
        try
        {
            _reconnectNow.Release();
        }
        catch (SemaphoreFullException)
        {
            // A reconnect is already pending.
        }
    }

    public async Task StartVpn(CancellationToken ct = default)
    {
        using var _ = await _operationLock.LockAsync(ct);
        _vpnWanted = true;
        await StartVpnLocked(ct);
    }

    public async Task StopVpn(CancellationToken ct = default)
    {
        using var _ = await _operationLock.LockAsync(ct);
        _vpnWanted = false;
        AssertRpcConnected();

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopping; });

        ServiceMessage reply;
        try
        {
            reply = await _speaker!.SendRequestAwaitReply(new ClientMessage
            {
                Stop = new StopRequest(),
            }, ct);
        }
        catch (Exception e)
        {
            throw new RpcOperationException("Failed to send stop command to service", e);
        }
        finally
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
        }

        if (reply.MsgCase != ServiceMessage.MsgOneofCase.Stop)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to stop VPN. Unexpected reply message type: {reply.MsgCase}");
        }

        if (!reply.Stop.Success)
        {
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to stop VPN. Service reported failure: {reply.Stop.ErrorMessage}");
        }

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetimeCts.CancelAsync();
        if (_connectionLoop != null)
            await _connectionLoop;
        await DisposeSpeaker();
        GC.SuppressFinalize(this);
    }

    private async Task KeepConnected(CancellationToken ct)
    {
        var retryDelay = MinRetryDelay;
        var failedAttempts = 0;
        try
        {
            while (true)
            {
                Task<Exception> connectionLost;
                try
                {
                    connectionLost = await Connect(ct);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    failedAttempts++;
                    // Log only the first failure: the service can be down for
                    // a long time.
                    if (failedAttempts == 1)
                        _logger.LogWarning(e, "Failed to connect to the RPC server; retrying until it is available");
                    else
                        _logger.LogDebug(e, "RPC connection attempt {Attempt} failed", failedAttempts);

                    await _reconnectNow.WaitAsync(retryDelay, ct);
                    retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                    continue;
                }

                _logger.LogInformation("Connected to the RPC server after {FailedAttempts} failed attempts",
                    failedAttempts);
                failedAttempts = 0;
                retryDelay = MinRetryDelay;
                // Drop a ReconnectNow request that came in during the attempt.
                _reconnectNow.Wait(0);

                await RestoreVpn(ct);
                var error = await connectionLost.WaitAsync(ct);
                _logger.LogWarning(error, "Lost the connection to the RPC server; reconnecting");
                await Disconnect(error, ct);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // The controller is being disposed.
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "RPC connection loop stopped unexpectedly");
        }
    }

    // Returns a task that completes with the error that ends the connection.
    private async Task<Task<Exception>> Connect(CancellationToken ct)
    {
        using var _ = await _operationLock.LockAsync(ct);
        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Connecting;
            state.VpnLifecycle = VpnLifecycle.Unknown;
            state.Workspaces = [];
            state.Agents = [];
        });

        var connectionLost = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var stream = await _transport.ConnectAsync(ct);
            _speaker = new Speaker<ClientMessage, ServiceMessage>(stream);
            _speaker.Receive += SpeakerOnReceive;
            _speaker.Error += e => connectionLost.TrySetResult(e);
            await _speaker.StartAsync(ct);

            var statusReply = await _speaker.SendRequestAwaitReply(new ClientMessage
            {
                Status = new StatusRequest(),
            }, ct);
            if (statusReply.MsgCase != ServiceMessage.MsgOneofCase.Status)
                throw new VpnLifecycleException(
                    $"Failed to get VPN status. Unexpected reply message type: {statusReply.MsgCase}");
            ApplyStatusUpdate(statusReply.Status);
        }
        catch (Exception e)
        {
            await DisposeSpeaker();
            MutateState(state =>
            {
                state.RpcLifecycle = RpcLifecycle.Disconnected;
                state.ConnectionError = e.Message;
            });
            throw;
        }

        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Connected;
            state.ConnectionError = null;
        });
        return connectionLost.Task;
    }

    private async Task Disconnect(Exception error, CancellationToken ct)
    {
        // Requests on a closed connection fail at once, so an operation that
        // holds the lock releases it soon.
        using var _ = await _operationLock.LockAsync(ct);
        await DisposeSpeaker();
        MutateState(state =>
        {
            state.RpcLifecycle = RpcLifecycle.Disconnected;
            state.ConnectionError = error.Message;
            state.VpnLifecycle = VpnLifecycle.Unknown;
            state.Workspaces = [];
            state.Agents = [];
        });
    }

    // Starts the VPN if it should run but the service reports it as stopped,
    // for example after a service restart or at app launch.
    private async Task RestoreVpn(CancellationToken ct)
    {
        using var _ = await _operationLock.LockAsync(ct);
        if (!_vpnWanted || GetState().VpnLifecycle != VpnLifecycle.Stopped)
            return;

        var credentialState = _credentialManager.GetCachedCredentials().State;
        if (credentialState != CredentialState.Valid)
        {
            // Do not start the VPN later with credentials that the user adds
            // after this point.
            _vpnWanted = false;
            _logger.LogInformation("Not starting the VPN after connecting: credentials are {State}", credentialState);
            return;
        }

        _logger.LogInformation("Starting the VPN after connecting to the RPC server");
        try
        {
            await StartVpnLocked(ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Failed to start the VPN after connecting to the RPC server");
        }
    }

    private async Task StartVpnLocked(CancellationToken ct)
    {
        AssertRpcConnected();

        var credentials = _credentialManager.GetCachedCredentials();
        if (credentials.State != CredentialState.Valid)
            throw new RpcOperationException(
                $"Cannot start VPN without valid credentials, current state: {credentials.State}");

        MutateState(state =>
        {
            state.VpnLifecycle = VpnLifecycle.Starting;
        });

        ServiceMessage reply;
        try
        {
            reply = await _speaker!.SendRequestAwaitReply(new ClientMessage
            {
                Start = new StartRequest
                {
                    CoderUrl = credentials.CoderUrl?.ToString(),
                    ApiToken = credentials.ApiToken,
                },
            }, ct);
        }
        catch (Exception e)
        {
            // Keep _vpnWanted: the connection failed, not the VPN, so the VPN
            // starts again after the reconnect.
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new RpcOperationException("Failed to send start command to service", e);
        }

        if (reply.MsgCase != ServiceMessage.MsgOneofCase.Start)
        {
            _vpnWanted = false;
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Unknown; });
            throw new VpnLifecycleException($"Failed to start VPN. Unexpected reply message type: {reply.MsgCase}");
        }

        if (!reply.Start.Success)
        {
            _vpnWanted = false;
            // We use Stopped instead of Unknown here as it's usually the case
            // that a failed start got cleaned up successfully.
            MutateState(state => { state.VpnLifecycle = VpnLifecycle.Stopped; });
            throw new VpnLifecycleException(
                $"Failed to start VPN. Service reported failure: {reply.Start.ErrorMessage}");
        }

        MutateState(state => { state.VpnLifecycle = VpnLifecycle.Started; });
    }

    private void MutateState(Action<RpcModel> mutator)
    {
        RpcModel newState;
        using (_stateLock.Lock())
        {
            mutator(_state);
            newState = _state.Clone();
        }

        StateChanged?.Invoke(this, newState);
    }

    private void ApplyStatusUpdate(Status status)
    {
        MutateState(state =>
        {
            state.VpnLifecycle = status.Lifecycle switch
            {
                Status.Types.Lifecycle.Unknown => VpnLifecycle.Unknown,
                Status.Types.Lifecycle.Starting => VpnLifecycle.Starting,
                Status.Types.Lifecycle.Started => VpnLifecycle.Started,
                Status.Types.Lifecycle.Stopping => VpnLifecycle.Stopping,
                Status.Types.Lifecycle.Stopped => VpnLifecycle.Stopped,
                _ => VpnLifecycle.Stopped,
            };
            state.Workspaces = status.PeerUpdate.UpsertedWorkspaces;
            state.Agents = status.PeerUpdate.UpsertedAgents;
        });

        _logger.LogInformation("RPC status update lifecycle={Lifecycle} workspaces={WorkspaceCount} agents={AgentCount}",
            status.Lifecycle,
            status.PeerUpdate.UpsertedWorkspaces.Count,
            status.PeerUpdate.UpsertedAgents.Count);
    }

    private void ApplyStartProgressUpdate(StartProgress message)
    {
        MutateState(state =>
        {
            // The model itself will ignore this value if we're not in the
            // starting state.
            state.VpnStartupProgress = VpnStartupProgress.FromProto(message);
        });
    }

    private void SpeakerOnReceive(ReplyableRpcMessage<ClientMessage, ServiceMessage> message)
    {
        if (message.Message.MsgCase is not ServiceMessage.MsgOneofCase.StartProgress)
            _logger.LogInformation("RPC receive msgCase={MsgCase}", message.Message.MsgCase);

        switch (message.Message.MsgCase)
        {
            case ServiceMessage.MsgOneofCase.Start:
            case ServiceMessage.MsgOneofCase.Stop:
            case ServiceMessage.MsgOneofCase.Status:
                ApplyStatusUpdate(message.Message.Status);
                break;
            case ServiceMessage.MsgOneofCase.StartProgress:
                ApplyStartProgressUpdate(message.Message.StartProgress);
                break;
            case ServiceMessage.MsgOneofCase.None:
            default:
                // TODO: log unexpected message
                break;
        }
    }

    private async Task DisposeSpeaker()
    {
        if (_speaker == null) return;
        _speaker.Receive -= SpeakerOnReceive;
        try
        {
            await _speaker.DisposeAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to dispose the RPC speaker");
        }

        _speaker = null;
    }

    private void AssertRpcConnected()
    {
        if (_speaker == null)
            throw new InvalidOperationException("Not connected to the RPC server");
    }
}

using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public partial class TrayWindowDisconnectedViewModel : ObservableObject
{
    private readonly IRpcController _rpcController;
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public TrayWindowDisconnectedViewModel(IRpcController rpcController, IDispatcher dispatcher)
    {
        _rpcController = rpcController;
        _dispatcher = dispatcher;
        _rpcController.StateChanged += (_, rpcModel) => UpdateFromRpcModel(rpcModel);
        UpdateFromRpcModel(_rpcController.GetState());
    }

    private void UpdateFromRpcModel(RpcModel rpcModel)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => UpdateFromRpcModel(rpcModel));
            return;
        }

        ErrorMessage = rpcModel.ConnectionError;
    }

    [RelayCommand]
    private void Reconnect()
    {
        _rpcController.ReconnectNow();
    }
}

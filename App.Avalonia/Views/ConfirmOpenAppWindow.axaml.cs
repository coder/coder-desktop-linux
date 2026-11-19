using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Coder.Desktop.App.Views;

public partial class ConfirmOpenAppWindow : Window
{
    private readonly TaskCompletionSource<(bool Open, bool DontAskAgain)> _result = new();

    public string Prompt { get; }
    public string DontAskAgainLabel { get; }

    public Task<(bool Open, bool DontAskAgain)> Result => _result.Task;

    public ConfirmOpenAppWindow() : this("(no name)", "")
    {
    }

    public ConfirmOpenAppWindow(string appName, string scheme)
    {
        InitializeComponent();

        Prompt = $"Open {appName}?\n\nThis opens a {scheme}: link in another app on your computer.";
        DontAskAgainLabel = $"Don't ask again for {scheme}: links";
        DataContext = this;

        Closed += (_, _) => _result.TrySetResult((false, false));
    }

    private void OpenClicked(object? sender, RoutedEventArgs e)
    {
        _result.TrySetResult((true, DontAskAgainCheckBox.IsChecked == true));
        Close();
    }

    private void CancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

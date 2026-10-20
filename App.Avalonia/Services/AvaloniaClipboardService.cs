using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Coder.Desktop.App.Services;

namespace Coder.Desktop.App;

public class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.Windows.FirstOrDefault()?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
    }
}

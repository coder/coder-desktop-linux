using System.Diagnostics;
using Coder.Desktop.App.Services;

namespace Coder.Desktop.App;

public class ProcessLauncherService : ILauncherService
{
    public Task LaunchUriAsync(Uri uri)
    {
        // OriginalString is the link that was checked; ToString() unescapes it.
        Process.Start(new ProcessStartInfo(uri.OriginalString)
        {
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }
}

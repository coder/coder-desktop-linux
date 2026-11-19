using Coder.Desktop.App.Models;

namespace Coder.Desktop.App.Services;

public interface ISettingsManager<T> where T : ISettings<T>, new()
{
    Task<T> Read(CancellationToken ct = default);
    Task Write(T settings, CancellationToken ct = default);
    /// <summary>
    /// Reads, changes and writes the settings. Updates run one at a time, so
    /// concurrent updates don't overwrite each other.
    /// </summary>
    Task Update(Action<T> update, CancellationToken ct = default);
}

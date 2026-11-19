using System;
using Coder.Desktop.App.Models;
using Coder.Desktop.App.Services;
using Coder.Desktop.App.Utils;
using Coder.Desktop.CoderSdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Coder.Desktop.App.ViewModels;

public interface IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl, bool confirmBeforeOpening);
}

public class AgentAppViewModelFactory(
    ILogger<AgentAppViewModel> childLogger,
    ICredentialManager credentialManager,
    ILauncherService launcherService,
    IWindowService windowService,
    ISettingsManager<AppLinkSettings> appLinkSettingsManager)
    : IAgentAppViewModelFactory
{
    public AgentAppViewModel Create(Uuid id, string name, Uri appUri, Uri? iconUrl, bool confirmBeforeOpening)
    {
        var cred = credentialManager.GetCachedCredentials();
        return new AgentAppViewModel(childLogger, credentialManager, launcherService, windowService,
            appLinkSettingsManager)
        {
            Id = id,
            Name = name,
            AppUri = appUri,
            IconUrl = iconUrl,
            // OriginalString is the raw link; ToString() unescapes it.
            BlockReason = cred.CoderUrl is null
                ? null
                : SessionTokenLink.BlockReason(appUri.OriginalString, cred.CoderUrl, cred.DashboardUrl),
            ConfirmBeforeOpening = confirmBeforeOpening,
        };
    }
}

public partial class AgentAppViewModel : ObservableObject, IModelUpdateable<AgentAppViewModel>
{
    private readonly ILogger<AgentAppViewModel> _logger;
    private readonly ICredentialManager _credentialManager;
    private readonly ILauncherService _launcherService;
    private readonly IWindowService _windowService;
    private readonly ISettingsManager<AppLinkSettings> _appLinkSettingsManager;

    public required Uuid Id { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private string _name = default!;

    public required Uri AppUri { get; init; }

    /// <summary>Set when the link needs a session token but isn't trusted with one.</summary>
    public string? BlockReason { get; init; }

    public bool ConfirmBeforeOpening { get; init; }

    public bool CanOpen => BlockReason is null;

    public double IconOpacity => CanOpen ? 1.0 : 0.4;

    [ObservableProperty]
    private Uri? _iconUrl = default!;

    /// <summary>
    /// UI framework-specific image type (Avalonia IImage, WinUI ImageSource, etc.).
    ///
    /// In App.Shared we keep this as <see cref="object"/>.
    /// </summary>
    [ObservableProperty]
    private object? _iconImageSource = default!;

    [ObservableProperty]
    private bool _useFallbackIcon = true;

    private string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(no name)" : Name;

    public string Details => DisplayName + ":\n\n" + (BlockReason ?? AppUri.ToString());

    public AgentAppViewModel(
        ILogger<AgentAppViewModel> logger,
        ICredentialManager credentialManager,
        ILauncherService launcherService,
        IWindowService windowService,
        ISettingsManager<AppLinkSettings> appLinkSettingsManager)
    {
        _logger = logger;
        _credentialManager = credentialManager;
        _launcherService = launcherService;
        _windowService = windowService;
        _appLinkSettingsManager = appLinkSettingsManager;

        // Apply the icon URL to the icon image source when it is updated.
        IconImageSource = UpdateIcon();
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IconUrl))
                IconImageSource = UpdateIcon();
        };
    }

    public bool TryApplyChanges(AgentAppViewModel obj)
    {
        // Replace the item when the link changes. Uri equality ignores the
        // fragment, which holds Gateway's params.
        if (Id != obj.Id || AppUri.OriginalString != obj.AppUri.OriginalString || BlockReason != obj.BlockReason ||
            ConfirmBeforeOpening != obj.ConfirmBeforeOpening)
            return false;

        // To avoid spurious UI updates which cause flashing, don't actually
        // write to values unless they've changed.
        if (Name != obj.Name)
            Name = obj.Name;
        if (IconUrl != obj.IconUrl)
        {
            UseFallbackIcon = true;
            IconUrl = obj.IconUrl;
        }

        return true;
    }

    private object? UpdateIcon()
    {
        if (IconUrl is null || (IconUrl.Scheme != "http" && IconUrl.Scheme != "https"))
        {
            UseFallbackIcon = true;
            return null;
        }

        // In App.Shared we don't construct UI-framework image types.
        // The UI layer can decide how to load/render this URI (SVG/bitmap/etc).
        UseFallbackIcon = true;
        return IconUrl;
    }

    public void OnImageOpened(object? sender, EventArgs e)
    {
        UseFallbackIcon = false;
    }

    public void OnImageFailed(object? sender, EventArgs e)
    {
        UseFallbackIcon = true;
    }

    [RelayCommand]
    private async Task OpenApp(object? parameter)
    {
        try
        {
            var uri = AppUri;

            // http and https URLs should already be filtered out by
            // AgentViewModel, but as a second line of defence don't do session
            // token var replacement on those URLs.
            if (uri.Scheme is not "http" and not "https")
            {
                // Recheck, since credentials may have loaded after this app was created.
                var cred = _credentialManager.GetCachedCredentials();
                if (cred is { State: CredentialState.Valid, CoderUrl: not null, ApiToken: not null })
                {
                    var reason = SessionTokenLink.BlockReason(uri.OriginalString, cred.CoderUrl, cred.DashboardUrl);
                    if (reason is not null) throw new Exception(reason);
                    uri = new Uri(uri.OriginalString.Replace(SessionTokenLink.Placeholder, cred.ApiToken));
                }
            }

            if (uri.ToString().Contains(SessionTokenLink.Placeholder))
                throw new Exception(
                    $"URI contains {SessionTokenLink.Placeholder} variable but could not be replaced (http and https URLs cannot contain {SessionTokenLink.Placeholder})");

            var scheme = uri.Scheme.ToLowerInvariant();
            if (!ConfirmBeforeOpening || (await _appLinkSettingsManager.Read()).TrustedSchemes.Contains(scheme))
            {
                await _launcherService.LaunchUriAsync(uri);
                return;
            }

            // Await the choice so this command, which doesn't run concurrently,
            // covers the whole flow and can't stack a second prompt.
            var (open, dontAskAgain) = await _windowService.ConfirmOpenAppAsync(DisplayName, scheme);
            if (!open) return;

            await _launcherService.LaunchUriAsync(uri);
            if (!dontAskAgain) return;
            try
            {
                await _appLinkSettingsManager.Update(settings => settings.TrustedSchemes.Add(scheme));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "could not save trusted app link scheme");
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "could not parse or launch app");
            _windowService.ShowMessageWindow("Could not open app", e.Message, "Coder Connect");
        }
    }
}

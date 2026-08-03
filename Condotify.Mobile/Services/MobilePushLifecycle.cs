using Condotify.Models;
using Condotify.Mobile.Core;
using Condotify.Services;

namespace Condotify.Mobile.Services;

public interface IMobilePushTokenProvider
{
    event Action<string>? TokenChanged;
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class UnconfiguredPushTokenProvider : IMobilePushTokenProvider
{
    public event Action<string>? TokenChanged { add { } remove { } }
    public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
}

public sealed class MobilePushLifecycle
{
    private readonly CondotifyApiClient _api;
    private readonly MobileDeviceContext _device;
    private readonly IMobilePushTokenProvider _tokens;
    private readonly MobileSessionCoordinator _session;

    public MobilePushLifecycle(CondotifyApiClient api, MobileDeviceContext device, IMobilePushTokenProvider tokens, MobileSessionCoordinator session)
    {
        _api = api;
        _device = device;
        _tokens = tokens;
        _session = session;
        _tokens.TokenChanged += TokenChanged;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return;
        await RegisterTokenAsync(token, cancellationToken);
    }

    private async Task RegisterTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || string.IsNullOrWhiteSpace(token)) return;
        await _api.RegisterMobileInstallationAsync(_device.InstallationId, new MobileInstallationUpsertViewModel
        {
            PushToken = token,
            Platform = DeviceInfo.Current.Platform == DevicePlatform.iOS ? MobilePlatform.Ios :
                DeviceInfo.Current.Platform == DevicePlatform.Android ? MobilePlatform.Android : MobilePlatform.Windows,
            DeviceName = _device.DeviceLabel,
            AppVersion = _device.AppVersion,
            Locale = System.Globalization.CultureInfo.CurrentUICulture.Name,
            TimeZone = TimeZoneInfo.Local.Id
        }, cancellationToken);
    }

    public async Task UnregisterAsync(CancellationToken cancellationToken = default) =>
        _ = await _api.UnregisterMobileInstallationAsync(_device.InstallationId, cancellationToken);

    private void TokenChanged(string token) => _ = RegisterTokenAsync(token);
}

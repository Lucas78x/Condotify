#if FIREBASE_CONFIGURED
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;

namespace Condotify.Mobile.Services;

public sealed class FirebasePushTokenProvider : IMobilePushTokenProvider, IDisposable
{
    private const string TokenKey = "condotify.fcm-token";
    private readonly MobileDeepLinkState _deepLinks;
    private readonly IFirebaseCloudMessaging? _messaging;

    public FirebasePushTokenProvider(MobileDeepLinkState deepLinks)
    {
        _deepLinks = deepLinks;
        try
        {
            if (!CrossFirebaseCloudMessaging.IsSupported) return;
            _messaging = CrossFirebaseCloudMessaging.Current;
            _messaging.TokenChanged += OnTokenChanged;
            _messaging.NotificationTapped += OnNotificationTapped;
        }
        catch
        {
            _messaging = null;
        }
    }

    public event Action<string>? TokenChanged;

    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_messaging is not null)
            {
                await _messaging.CheckIfValidAsync();
                var current = await _messaging.GetTokenAsync();
                if (!string.IsNullOrWhiteSpace(current))
                {
                    await SecureStorage.Default.SetAsync(TokenKey, current);
                    return current;
                }
            }
        }
        catch
        {
            // Inbox and the rest of the app remain available when FCM is not configured.
        }

        try { return await SecureStorage.Default.GetAsync(TokenKey); }
        catch { return null; }
    }

    private void OnTokenChanged(object? sender, FCMTokenChangedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Token)) return;
        _ = SecureStorage.Default.SetAsync(TokenKey, args.Token);
        TokenChanged?.Invoke(args.Token);
    }

    private void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs args)
    {
        var data = args.Notification.Data;
        if (data.TryGetValue("deepLink", out var deepLink) && _deepLinks.Publish(deepLink)) return;
        if (data.TryGetValue("route", out var route)) _deepLinks.Publish(route);
    }

    public void Dispose()
    {
        if (_messaging is null) return;
        _messaging.TokenChanged -= OnTokenChanged;
        _messaging.NotificationTapped -= OnNotificationTapped;
    }
}
#endif

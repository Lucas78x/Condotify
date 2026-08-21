using Condotify.Mobile.Core;
using Condotify.Mobile.Services;
using Condotify.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MudBlazor.Services;
using System.Reflection;
#if ANDROID || IOS
using ZXing.Net.Maui.Controls;
#endif
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Condotify.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
#if ANDROID || IOS
            .UseBarcodeReader()
#endif
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));
        ConfigureFirebase(builder);

        var configuredBaseUrl = Environment.GetEnvironmentVariable("CONDOTIFY_API_BASE_URL")
            ?? typeof(MauiProgram).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(x => x.Key == "CondotifyApiBaseUrl")?.Value;
#if ANDROID
        var baseUrl = configuredBaseUrl ?? "http://10.0.2.2:5093";
#else
        var baseUrl = configuredBaseUrl ?? "https://localhost:7118";
#endif
        ConfigureMediaTransport(baseUrl);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CondotifyApi:BaseUrl"] = baseUrl
        });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddHttpClient("CondotifyApi", client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        builder.Services.AddHttpClient("CondotifyAuth", client => client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"));
        builder.Services.AddHttpClient("OpenMeteo", client =>
        {
            client.BaseAddress = new Uri("https://api.open-meteo.com/");
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        builder.Services.AddMudServices(options =>
            options.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter);
        builder.Services.AddSingleton<IMobileSessionVault, MauiSessionVault>();
        builder.Services.AddSingleton<MobileSessionCoordinator>();
        builder.Services.AddSingleton<ISessionContextProvider>(service => service.GetRequiredService<MobileSessionCoordinator>());
        builder.Services.AddSingleton<CondotifyApiClient>();
        builder.Services.AddSingleton<MobileAppState>();
        builder.Services.AddSingleton<MobileDeviceContext>();
        builder.Services.AddSingleton<MobileWeatherService>();
#if FIREBASE_CONFIGURED
        builder.Services.AddSingleton<IMobilePushTokenProvider, FirebasePushTokenProvider>();
#else
        builder.Services.AddSingleton<IMobilePushTokenProvider, UnconfiguredPushTokenProvider>();
#endif
        builder.Services.AddSingleton<MobilePushLifecycle>();
        builder.Services.AddSingleton<MobileNotificationSettings>();
        builder.Services.AddSingleton<MobileDeepLinkState>();
        builder.Services.AddSingleton<MobileConnectivityState>();
        builder.Services.AddSingleton<MobileAppearanceState>();
        builder.Services.AddSingleton<MobileAppLockService>();
        builder.Services.AddSingleton<MobileBiometricService>();
        builder.Services.AddSingleton<MobileOfflineCache>();
        builder.Services.AddSingleton<MobileOfflineProtectedStore>();
        builder.Services.AddSingleton<MobileOfflineOperationsService>();
        builder.Services.AddSingleton<MobileDeviceActions>();
        builder.Services.AddSingleton<MobileQrScanner>();
        builder.Services.AddSingleton<MobilePullToRefreshState>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    private static void ConfigureMediaTransport(string baseUrl)
    {
#if WINDOWS
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiUri)
            || !string.Equals(apiUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return;

        const string allowInsecureMedia = "--allow-running-insecure-content";
        var currentArguments = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? string.Empty;
        if (!currentArguments.Contains(allowInsecureMedia, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                string.Join(' ', new[] { currentArguments.Trim(), allowInsecureMedia }.Where(x => x.Length > 0)));
        }
#elif ANDROID
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiUri)
            || !string.Equals(apiUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return;

        Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping(
            "AllowConfiguredHttpMedia",
            (handler, _) => handler.PlatformView.Settings.MixedContentMode = Android.Webkit.MixedContentHandling.AlwaysAllow);
#endif
    }

    private static void ConfigureFirebase(MauiAppBuilder builder)
    {
        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID && FIREBASE_CONFIGURED
            events.AddAndroid(android => android.OnCreate((activity, _) =>
            {
                try { Plugin.Firebase.Core.Platforms.Android.CrossFirebase.Initialize(activity, () => activity); }
                catch { }
            }));
#elif IOS && FIREBASE_CONFIGURED
            events.AddiOS(ios => ios.WillFinishLaunching((_, _) =>
            {
                try
                {
                    Plugin.Firebase.Core.Platforms.iOS.CrossFirebase.Initialize();
                    Plugin.Firebase.CloudMessaging.FirebaseCloudMessagingImplementation.Initialize();
                }
                catch { }
                return true;
            }));
#elif WINDOWS
            events.AddWindows(windows => windows.OnWindowCreated(window =>
            {
                if (window.Content is Microsoft.UI.Xaml.FrameworkElement root)
                    root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Light;

                var titleBar = window.AppWindow.TitleBar;
                var background = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
                var foreground = global::Windows.UI.Color.FromArgb(255, 28, 36, 49);
                var hover = global::Windows.UI.Color.FromArgb(255, 243, 245, 248);
                titleBar.BackgroundColor = background;
                titleBar.ForegroundColor = foreground;
                titleBar.InactiveBackgroundColor = background;
                titleBar.InactiveForegroundColor = foreground;
                titleBar.ButtonBackgroundColor = background;
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = foreground;
                titleBar.ButtonPressedBackgroundColor = hover;
                titleBar.ButtonPressedForegroundColor = foreground;
                ConfigureWindowsCaption(window);
            }));
#endif
        });
    }

#if WINDOWS
    private static void ConfigureWindowsCaption(Microsoft.UI.Xaml.Window window)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var lightMode = 0;
        var captionColor = 0x00FFFFFF;
        var textColor = 0x0031241C;
        var borderColor = 0x00EAE3DD;
        DwmSetWindowAttribute(handle, 20, ref lightMode, sizeof(int));
        DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);
#endif
}

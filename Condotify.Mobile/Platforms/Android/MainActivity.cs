using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Condotify.Mobile.Services;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.CloudMessaging;
#endif

namespace Condotify.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "app.condotify.com.br",
    DataPathPrefix = "/app",
    AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplySafeAreaInsets();
        ConfigureNotifications();
#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
#endif
        Publish(Intent);
    }

    private void ApplySafeAreaInsets()
    {
        var contentView = FindViewById(Android.Resource.Id.Content);
        if (contentView is null) return;

        contentView.SetBackgroundColor(new Android.Graphics.Color(GetColor(Resource.Color.colorPrimary)));

        if (Window is { DecorView: { } decorView } window)
        {
            var systemBarController = WindowCompat.GetInsetsController(window, decorView);
            if (systemBarController is not null)
            {
                systemBarController.AppearanceLightStatusBars = false;
                systemBarController.AppearanceLightNavigationBars = false;
            }
        }

        ViewCompat.SetOnApplyWindowInsetsListener(contentView, new SafeAreaInsetsListener());
        ViewCompat.RequestApplyInsets(contentView);
    }

    private sealed class SafeAreaInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? windowInsets)
        {
            if (view is null || windowInsets is null) return windowInsets;

            var safeArea = windowInsets.GetInsets(
                WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            if (safeArea is null) return windowInsets;

            view.SetPadding(safeArea.Left, safeArea.Top, safeArea.Right, safeArea.Bottom);
            return WindowInsetsCompat.Consumed;
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.OnNewIntent(intent);
#endif
        Publish(intent);
    }

    private static void Publish(Intent? intent)
    {
        if (string.IsNullOrWhiteSpace(intent?.DataString)) return;
        IPlatformApplication.Current?.Services.GetService<MobileDeepLinkState>()?.Publish(intent.DataString);
    }

    private void ConfigureNotifications()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var channelId = $"{PackageName}.general";
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(channelId, "Avisos da F&F Access", NotificationImportance.Default));
#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.ChannelId = channelId;
#endif
    }
}

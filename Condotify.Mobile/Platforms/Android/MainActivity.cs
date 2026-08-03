using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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
        ConfigureNotifications();
#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
#endif
        Publish(Intent);
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
        manager?.CreateNotificationChannel(new NotificationChannel(channelId, "Avisos do Condotify", NotificationImportance.Default));
#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.ChannelId = channelId;
#endif
    }
}

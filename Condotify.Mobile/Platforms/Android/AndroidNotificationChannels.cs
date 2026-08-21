using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.App;
#if FIREBASE_CONFIGURED
using Plugin.Firebase.CloudMessaging;
#endif

namespace Condotify.Mobile;

internal static class AndroidNotificationChannels
{
    private const string AudibleChannelSuffix = ".alerts.v2";
    private const string SilentChannelSuffix = ".alerts.silent";

    public static void Configure(Context context, bool soundEnabled)
    {
        var packageName = context.PackageName ?? "br.com.condotify.app";
        var audibleChannelId = packageName + AudibleChannelSuffix;
        var silentChannelId = packageName + SilentChannelSuffix;
        var soundUri = Android.Net.Uri.Parse(
            $"android.resource://{packageName}/{Resource.Raw.ff_access_notification}");

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager is null) return;

            var audioAttributes = new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Notification)!
                .SetContentType(AudioContentType.Sonification)!
                .Build();

            var audibleChannel = new NotificationChannel(
                audibleChannelId,
                "Avisos da F&F Access",
                NotificationImportance.Default)
            {
                Description = "Notificações com um aviso sonoro suave"
            };
            audibleChannel.SetSound(soundUri, audioAttributes);
            audibleChannel.EnableVibration(false);

            var silentChannel = new NotificationChannel(
                silentChannelId,
                "Avisos silenciosos da F&F Access",
                NotificationImportance.Default)
            {
                Description = "Notificações sem som e sem vibração"
            };
            silentChannel.SetSound(null, null);
            silentChannel.EnableVibration(false);

            manager.CreateNotificationChannel(audibleChannel);
            manager.CreateNotificationChannel(silentChannel);
        }

#if FIREBASE_CONFIGURED
        FirebaseCloudMessagingImplementation.ChannelId = soundEnabled
            ? audibleChannelId
            : silentChannelId;
        FirebaseCloudMessagingImplementation.NotificationBuilderProvider = notification =>
        {
            var channelId = soundEnabled ? audibleChannelId : silentChannelId;
            var builder = new NotificationCompat.Builder(context, channelId);
            builder.SetSmallIcon(Android.Resource.Drawable.SymDefAppIcon);
            builder.SetContentTitle(ReadText(notification.Title, notification.Data, "title"));
            builder.SetContentText(ReadText(notification.Body, notification.Data, "body"));
            builder.SetPriority(NotificationCompat.PriorityDefault);
            builder.SetAutoCancel(true);
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                builder.SetSound(soundEnabled ? soundUri : null);
            return builder;
        };
#endif
    }

#if FIREBASE_CONFIGURED
    private static string ReadText(
        string? notificationValue,
        IDictionary<string, string>? data,
        string key)
    {
        if (!string.IsNullOrWhiteSpace(notificationValue)) return notificationValue;
        return data is not null && data.TryGetValue(key, out var value) ? value : string.Empty;
    }
#endif
}

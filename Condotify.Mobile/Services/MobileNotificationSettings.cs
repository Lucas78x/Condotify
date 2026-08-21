using Microsoft.Maui.Storage;

namespace Condotify.Mobile.Services;

public sealed class MobileNotificationSettings
{
    internal const string SoundEnabledKey = "condotify.notifications.sound.enabled";

    public bool SoundEnabled { get; private set; } = Preferences.Default.Get(SoundEnabledKey, true);
    public event Action? Changed;

    public void SetSoundEnabled(bool enabled)
    {
        if (SoundEnabled == enabled) return;

        SoundEnabled = enabled;
        Preferences.Default.Set(SoundEnabledKey, enabled);
#if ANDROID
        AndroidNotificationChannels.Configure(Android.App.Application.Context, enabled);
#endif
        Changed?.Invoke();
    }
}

using Microsoft.Maui.Storage;

namespace Condotify.Mobile.Services;

public sealed class MobileAppearanceState
{
    private const string DarkModeKey = "condotify.dark-mode";
    private const string HapticsKey = "condotify.haptics";
    public bool IsDarkMode { get; private set; } = Preferences.Default.Get(DarkModeKey, false);
    public bool HapticsEnabled { get; private set; } = Preferences.Default.Get(HapticsKey, true);
    public event Action? Changed;

    public void SetDarkMode(bool enabled)
    {
        if (IsDarkMode == enabled) return;
        IsDarkMode = enabled;
        Preferences.Default.Set(DarkModeKey, enabled);
        try { Application.Current!.UserAppTheme = enabled ? AppTheme.Dark : AppTheme.Light; } catch { }
        Changed?.Invoke();
    }

    public void SetHaptics(bool enabled)
    {
        if (HapticsEnabled == enabled) return;
        HapticsEnabled = enabled;
        Preferences.Default.Set(HapticsKey, enabled);
        Changed?.Invoke();
    }
}

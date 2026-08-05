using Microsoft.Maui.Storage;

namespace Condotify.Mobile.Services;

public sealed class MobileAppLockService
{
    private const string EnabledKey = "condotify.app-lock.enabled";
    private const string TimeoutKey = "condotify.app-lock.timeout";
    private DateTimeOffset _lastUnlock = DateTimeOffset.MinValue;

    public bool Enabled { get; private set; } = Preferences.Default.Get(EnabledKey, false);
    public int TimeoutMinutes { get; private set; } = Preferences.Default.Get(TimeoutKey, 5);
    public event Action? Changed;
    public bool NeedsUnlock => Enabled && DateTimeOffset.UtcNow - _lastUnlock >= TimeSpan.FromMinutes(TimeoutMinutes);

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled) _lastUnlock = DateTimeOffset.UtcNow;
        else _lastUnlock = DateTimeOffset.MinValue;
        Preferences.Default.Set(EnabledKey, enabled);
        Changed?.Invoke();
    }

    public void SetTimeout(int minutes)
    {
        TimeoutMinutes = Math.Clamp(minutes, 1, 30);
        Preferences.Default.Set(TimeoutKey, TimeoutMinutes);
        Changed?.Invoke();
    }

    public void MarkUnlocked()
    {
        _lastUnlock = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    public void LockNow()
    {
        _lastUnlock = DateTimeOffset.MinValue;
        Changed?.Invoke();
    }
}

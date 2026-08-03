namespace Condotify.Mobile.Services;

public sealed class MobileDeviceContext
{
    public string DeviceLabel => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
    public string InstallationId
    {
        get
        {
            const string key = "condotify.installation-id";
            var value = Preferences.Default.Get(key, string.Empty);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            value = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(key, value);
            return value;
        }
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public string AppVersion => AppInfo.Current.VersionString;
}

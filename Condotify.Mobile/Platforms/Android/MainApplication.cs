using Android.App;
using Android.Runtime;
using Condotify.Mobile.Services;

namespace Condotify.Mobile;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();
        var soundEnabled = Preferences.Default.Get(MobileNotificationSettings.SoundEnabledKey, true);
        AndroidNotificationChannels.Configure(this, soundEnabled);
    }
}

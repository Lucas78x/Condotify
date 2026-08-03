using Condotify.Mobile.Services;
using Foundation;
using UIKit;

namespace Condotify.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool ContinueUserActivity(
        UIApplication application,
        NSUserActivity userActivity,
        UIApplicationRestorationHandler completionHandler)
    {
        var handled = IPlatformApplication.Current?.Services.GetService<MobileDeepLinkState>()
            ?.Publish(userActivity.WebPageUrl?.AbsoluteString) == true;
        return handled || base.ContinueUserActivity(application, userActivity, completionHandler);
    }
}

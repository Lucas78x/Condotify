using Condotify.Mobile.Services;

namespace Condotify.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = Preferences.Default.Get("condotify.dark-mode", false) ? AppTheme.Dark : AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "F&F Access" };
}

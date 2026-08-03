using Condotify.Mobile.Services;

namespace Condotify.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "Condotify" };
}

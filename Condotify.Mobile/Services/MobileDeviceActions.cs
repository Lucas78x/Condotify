using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Condotify.Mobile.Services;

public sealed class MobileDeviceActions
{
    private readonly MobileAppearanceState _appearance;
    public MobileDeviceActions(MobileAppearanceState appearance) => _appearance = appearance;

    public async Task<bool> RequestCameraAsync() =>
        await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted;

    public void Confirm()
    {
        if (!_appearance.HapticsEnabled) return;
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { }
    }

    public void Tap()
    {
        if (!_appearance.HapticsEnabled) return;
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { }
    }

    public Task ShareTextAsync(string title, string text) => Share.Default.RequestAsync(new ShareTextRequest
    {
        Title = title,
        Text = text
    });
}

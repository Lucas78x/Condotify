using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

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

    public Task CopyTextAsync(string text) => Clipboard.Default.SetTextAsync(text);

    public async Task SharePngDataUriAsync(string title, string dataUri, string fileName)
    {
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrWhiteSpace(dataUri) || !dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A imagem gerada para o compartilhamento não é válida.", nameof(dataUri));

        var bytes = Convert.FromBase64String(dataUri[prefix.Length..]);
        if (bytes.Length == 0 || bytes.Length > 12 * 1024 * 1024)
            throw new InvalidOperationException("A imagem gerada possui um tamanho inválido.");

        var safeFileName = string.Concat(fileName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        if (!safeFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) safeFileName += ".png";

        var path = Path.Combine(FileSystem.CacheDirectory, safeFileName);
        await File.WriteAllBytesAsync(path, bytes);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path, "image/png")
        });
    }
}

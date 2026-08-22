using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Condotify.Mobile.Services;

public sealed class MobileDeviceActions
{
    private const string ShareCachePrefix = "condotify-share-";
    private static readonly TimeSpan ShareCacheLifetime = TimeSpan.FromMinutes(10);
    private readonly MobileAppearanceState _appearance;

    public MobileDeviceActions(MobileAppearanceState appearance)
    {
        _appearance = appearance;
        CleanupShareCache();
    }

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

        await ShareTemporaryFileAsync(title, safeFileName, "image/png", bytes);
    }

    public async Task ShareTemporaryFileAsync(string title, string fileName, string contentType, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(bytes);

        var extension = Path.GetExtension(fileName);
        var path = Path.Combine(FileSystem.CacheDirectory, $"{ShareCachePrefix}{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = title,
                File = new ShareFile(path, contentType)
            });
            _ = DeleteShareFileLaterAsync(path);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static void CleanupShareCache()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(FileSystem.CacheDirectory, $"{ShareCachePrefix}*"))
                TryDelete(path);
        }
        catch { }
    }

    private static async Task DeleteShareFileLaterAsync(string path)
    {
        await Task.Delay(ShareCacheLifetime);
        TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}

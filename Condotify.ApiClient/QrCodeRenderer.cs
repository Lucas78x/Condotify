using QRCoder;

namespace Condotify.Services;

public static class QrCodeRenderer
{
    public static string ToPngDataUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var bytes = PngByteQRCodeHelper.GetQRCode(value, QRCodeGenerator.ECCLevel.Q, 12, drawQuietZones: true);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}

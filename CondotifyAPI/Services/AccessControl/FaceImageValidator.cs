namespace CondotifyAPI.Services.AccessControl;

public static class FaceImageValidator
{
    public static FaceImageValidationResult Validate(string value, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(value)) return new(false, "Adicione uma foto antes de ativar a facial.", 0, 0, 0);
        byte[] bytes;
        try
        {
            var normalized = value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value;
            bytes = Convert.FromBase64String(normalized);
        }
        catch { return new(false, "A foto enviada nao possui um formato valido.", 0, 0, 0); }

        if (bytes.Length > maxBytes) return new(false, $"A foto deve possuir no maximo {maxBytes / 1000} KB.", bytes.Length, 0, 0);
        if (!TryDimensions(bytes, out var width, out var height)) return new(false, "Envie uma imagem JPG ou PNG valida.", bytes.Length, 0, 0);
        if (width < 200 || height < 200) return new(false, "A foto deve possuir pelo menos 200 por 200 pixels.", bytes.Length, width, height);
        var ratio = width / (double)height;
        if (ratio is < 0.6 or > 1.65) return new(false, "Use uma foto frontal, sem recortes muito estreitos ou panoramicos.", bytes.Length, width, height);
        return new(true, string.Empty, bytes.Length, width, height);
    }

    private static bool TryDimensions(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            width = ReadBigEndian(data, 16);
            height = ReadBigEndian(data, 20);
            return width > 0 && height > 0;
        }
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;
        var offset = 2;
        while (offset + 8 < data.Length)
        {
            if (data[offset] != 0xFF) { offset++; continue; }
            var marker = data[offset + 1];
            if (marker is 0xD8 or 0xD9) { offset += 2; continue; }
            if (offset + 4 > data.Length) break;
            var length = data[offset + 2] * 256 + data[offset + 3];
            if (length < 2 || offset + length + 2 > data.Length) break;
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                height = data[offset + 5] * 256 + data[offset + 6];
                width = data[offset + 7] * 256 + data[offset + 8];
                return width > 0 && height > 0;
            }
            offset += length + 2;
        }
        return false;
    }

    private static int ReadBigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}

public sealed record FaceImageValidationResult(bool Success, string Error, int Bytes, int Width, int Height);

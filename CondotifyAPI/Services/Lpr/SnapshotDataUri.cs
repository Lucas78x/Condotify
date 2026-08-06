namespace CondotifyAPI.Services.Lpr;

// Derives the data: URI content type from the image's own magic bytes rather
// than trusting the camera's raw Content-Type header (which real devices
// sometimes report non-canonically, e.g. "image/jpg") - PrivateMediaStore
// only accepts an exact "image/jpeg"/"image/png"/"image/webp" match.
internal static class SnapshotDataUri
{
    internal static string? Build(byte[] content)
    {
        var contentType = DetectContentType(content);
        return contentType is null ? null : $"data:{contentType};base64,{Convert.ToBase64String(content)}";
    }

    private static string? DetectContentType(byte[] content)
    {
        if (content.Length < 4) return null;
        if (content[0] == 0xFF && content[1] == 0xD8) return "image/jpeg";
        if (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47) return "image/png";
        return null;
    }
}

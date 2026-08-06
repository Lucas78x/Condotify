using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class SnapshotDataUriTests
{
    private static readonly byte[] JpegMagicBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];
    private static readonly byte[] PngMagicBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    [Fact]
    public void Build_DetectsJpegFromMagicBytes()
    {
        var result = SnapshotDataUri.Build(JpegMagicBytes);

        Assert.StartsWith("data:image/jpeg;base64,", result);
        Assert.Equal(Convert.ToBase64String(JpegMagicBytes), result!["data:image/jpeg;base64,".Length..]);
    }

    [Fact]
    public void Build_DetectsPngFromMagicBytes()
    {
        var result = SnapshotDataUri.Build(PngMagicBytes);

        Assert.StartsWith("data:image/png;base64,", result);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 })]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF })]
    public void Build_ReturnsNull_ForUnrecognizedOrTooShortContent(byte[] content)
    {
        Assert.Null(SnapshotDataUri.Build(content));
    }
}

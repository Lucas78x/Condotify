using CondotifyAPI.Services.AccessControl;

namespace CondotifyAPI.Tests;

public class FaceImageValidatorTests
{
    [Fact]
    public void Validate_ShouldRejectInvalidBase64()
    {
        var result = FaceImageValidator.Validate("not-base64", 1_000_000);

        Assert.False(result.Success);
    }

    [Fact]
    public void Validate_ShouldRejectSmallImages()
    {
        var result = FaceImageValidator.Validate(PngHeader(120, 120), 1_000_000);

        Assert.False(result.Success);
        Assert.Contains("200", result.Error);
    }

    [Fact]
    public void Validate_ShouldAcceptPortraitImageWithMinimumQuality()
    {
        var result = FaceImageValidator.Validate(PngHeader(480, 640), 1_000_000);

        Assert.True(result.Success);
        Assert.Equal(480, result.Width);
        Assert.Equal(640, result.Height);
    }

    private static string PngHeader(int width, int height)
    {
        var data = new byte[24];
        data[0] = 0x89; data[1] = 0x50; data[2] = 0x4E; data[3] = 0x47;
        WriteBigEndian(data, 16, width);
        WriteBigEndian(data, 20, height);
        return Convert.ToBase64String(data);
    }

    private static void WriteBigEndian(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}

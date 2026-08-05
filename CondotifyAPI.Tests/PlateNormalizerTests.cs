using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class PlateNormalizerTests
{
    [Theory]
    [InlineData("ABC1234", "ABC1234")]
    [InlineData("abc1234", "ABC1234")]
    [InlineData("ABC-1234", "ABC1234")]
    [InlineData("ABC1D23", "ABC1D23")]
    [InlineData("abc 1d23", "ABC1D23")]
    public void Normalize_AcceptsOldAndMercosulFormats(string input, string expected)
    {
        var result = PlateNormalizer.Normalize(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AB1234")]
    [InlineData("ABCD1234")]
    [InlineData("ABC12345")]
    public void Normalize_RejectsInvalidInput(string input)
    {
        var result = PlateNormalizer.Normalize(input);

        Assert.Null(result);
    }

    [Fact]
    public void Normalize_RejectsNull()
    {
        Assert.Null(PlateNormalizer.Normalize(null));
    }
}

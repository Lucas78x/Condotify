using CondotifyAPI.Domain.Utilities;

namespace CondotifyAPI.Tests;

public sealed class UrlKeyGeneratorTests
{
    [Theory]
    [InlineData("DEMO-001", "Condomínio Demo", "demo-001")]
    [InlineData("Residencial São José", null, "residencial-sao-jose")]
    [InlineData("  Torre_A / 2026  ", null, "torre-a-2026")]
    [InlineData("---", "", "condominio")]
    public void Create_ReturnsSafeStableKey(string preferredValue, string? fallbackValue, string expected)
    {
        Assert.Equal(expected, UrlKeyGenerator.Create(preferredValue, fallbackValue));
    }

    [Fact]
    public void Create_LimitsKeyToDatabaseColumnLength()
    {
        var key = UrlKeyGenerator.Create(new string('A', 150));

        Assert.Equal(100, key.Length);
        Assert.All(key, character => Assert.Equal('a', character));
    }
}

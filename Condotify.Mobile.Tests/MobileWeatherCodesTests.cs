using Condotify.Mobile.Core;

namespace Condotify.Mobile.Tests;

public sealed class MobileWeatherCodesTests
{
    [Theory]
    [InlineData(0, "sunny")]
    [InlineData(1, "cloudy")]
    [InlineData(2, "cloudy")]
    [InlineData(3, "overcast")]
    [InlineData(45, "overcast")]
    [InlineData(61, "rain")]
    [InlineData(82, "rain")]
    [InlineData(71, "snow")]
    [InlineData(95, "storm")]
    [InlineData(99, "storm")]
    public void Describe_MapsKnownWmoCodesToExpectedCategory(int weatherCode, string expectedCategory)
    {
        var (_, category) = MobileWeatherCodes.Describe(weatherCode);

        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void Describe_UnknownCodeFallsBackToNeutralCategoryInsteadOfThrowing()
    {
        var (description, category) = MobileWeatherCodes.Describe(-1);

        Assert.Equal("unknown", category);
        Assert.False(string.IsNullOrWhiteSpace(description));
    }
}

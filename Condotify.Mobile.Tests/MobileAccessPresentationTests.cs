using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class MobileAccessPresentationTests
{
    private static readonly DateTime Now = new(2026, 8, 4, 10, 0, 0, DateTimeKind.Local);

    [Fact]
    public void MatchesVisitorView_SeparatesTodayUpcomingAndHistory()
    {
        var today = Visit(Now.AddHours(-1), Now.AddHours(2), "Expected");
        var upcoming = Visit(Now.AddDays(2), Now.AddDays(2).AddHours(3), "Expected");
        var history = Visit(Now.AddDays(-2), Now.AddDays(-2).AddHours(3), "CheckedOut");

        Assert.True(MobileAccessPresentation.MatchesVisitorView(today, MobileVisitorView.Today, Now));
        Assert.True(MobileAccessPresentation.MatchesVisitorView(upcoming, MobileVisitorView.Upcoming, Now));
        Assert.True(MobileAccessPresentation.MatchesVisitorView(history, MobileVisitorView.History, Now));
    }

    [Theory]
    [InlineData("e200 3412-abcd:0099", "E2003412ABCD0099")]
    [InlineData("0xA1B2C3D4", "A1B2C3D4")]
    [InlineData("", "")]
    public void NormalizeTagHex_RemovesPresentationCharacters(string input, string expected) =>
        Assert.Equal(expected, MobileAccessPresentation.NormalizeTagHex(input));

    [Theory]
    [InlineData("E2003412ABCD0099", "")]
    [InlineData("XYZ123", "Use somente números de 0 a 9 e letras de A a F.")]
    [InlineData("ABC123D", "O código hexadecimal precisa ter pares completos de caracteres.")]
    public void ValidateTagHex_ExplainsInvalidValues(string input, string expected) =>
        Assert.Equal(expected, MobileAccessPresentation.ValidateTagHex(input));

    private static ConciergeVisitViewModel Visit(DateTime from, DateTime to, string status) => new()
    {
        ValidFrom = from,
        ValidTo = to,
        Status = status
    };
}

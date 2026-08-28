using System.Text.RegularExpressions;

namespace Condotify.Web.Tests;

public sealed class PortalRefinementCssTests
{
    private static readonly Regex GlobalMudSelector = new(
        @"(?m)^\s*\.mud-(?:chip|dialog|popover|table)(?:-[\w-]+)?(?=\s*(?:[,>{+~]|$))",
        RegexOptions.CultureInvariant);

    [Fact]
    public void RefinementStyles_DoNotOverrideMudBlazorGlobally()
    {
        var cssPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "portal-refinement.css");
        var css = File.ReadAllText(cssPath);
        var matches = GlobalMudSelector.Matches(css)
            .Select(match => match.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            matches.Length == 0,
            $"Portal refinements must be opt-in. Global MudBlazor selectors found: {string.Join(", ", matches)}");
    }
}

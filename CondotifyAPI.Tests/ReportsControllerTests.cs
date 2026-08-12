using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CondotifyAPI.Tests;

public sealed class ReportsControllerTests
{
    [Theory]
    [InlineData(-1, 30)]
    [InlineData(30, 30)]
    [InlineData(31, 90)]
    [InlineData(90, 90)]
    [InlineData(91, 180)]
    [InlineData(180, 180)]
    [InlineData(181, 365)]
    [InlineData(999, 365)]
    public void Period_IsNormalizedToSupportedRanges(int requested, int expected) =>
        Assert.Equal(expected, ReportsController.NormalizePeriod(requested));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(8, 10, 80)]
    [InlineData(1, 3, 33.3)]
    public void Percentage_IsSafeAndRounded(int value, int total, decimal expected) =>
        Assert.Equal(expected, ReportsController.Percentage(value, total));

    [Fact]
    public void QualityScore_UsesAllQualitativeIndicators()
    {
        var indicators = new[]
        {
            new Condotify.Models.ReportQualityIndicatorViewModel { Percentage = 80 },
            new Condotify.Models.ReportQualityIndicatorViewModel { Percentage = 70 },
            new Condotify.Models.ReportQualityIndicatorViewModel { Percentage = 90 }
        };

        Assert.Equal(80, ReportsController.QualityScore(indicators));
    }

    [Fact]
    public void ReportEndpoint_RequiresDashboardPermission()
    {
        var method = typeof(ReportsController).GetMethod(nameof(ReportsController.Get), BindingFlags.Instance | BindingFlags.Public);
        var permission = Assert.Single(method!.GetCustomAttributes<RequireLicensePermissionAttribute>());

        Assert.Equal(LicensePermissionEnum.ViewDashboard, Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
        Assert.Equal("api/access/licenses/{licenseId:guid}/reports", typeof(ReportsController).GetCustomAttribute<RouteAttribute>()?.Template);
    }
}

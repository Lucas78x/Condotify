using CondotifyAPI.Controllers;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class AccessRoutesControllerTests
{
    [Fact]
    public void RouteOutputProjection_ShouldNotLoadEquipmentPassword()
    {
        Environment.SetEnvironmentVariable(
            "CONDOTIFY_EQUIPMENT_SECRET",
            "condotify-tests-equipment-secret-2026");

        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;

        using var context = new DatabaseContext(options);

        var sql = context.AccessRoutes
            .IgnoreQueryFilters()
            .IgnoreAutoIncludes()
            .Select(AccessRoutesController.RouteOutProjection)
            .ToQueryString();

        Assert.DoesNotContain("\"Password\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Name\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Type\"", sql, StringComparison.Ordinal);
    }
}

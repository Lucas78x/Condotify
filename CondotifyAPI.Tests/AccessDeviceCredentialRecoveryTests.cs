using CondotifyAPI.Controllers;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class AccessDeviceCredentialRecoveryTests
{
    [Fact]
    public void UpdateSnapshot_ShouldNotDecryptTheExistingPassword()
    {
        Environment.SetEnvironmentVariable(
            "CONDOTIFY_EQUIPMENT_SECRET",
            "condotify-tests-equipment-secret-2026");

        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        using var context = new DatabaseContext(options);

        var sql = LicenseStructureController
            .AccessDeviceUpdateQuery(context, Guid.NewGuid(), Guid.NewGuid())
            .IgnoreQueryFilters()
            .ToQueryString();

        Assert.DoesNotContain("\"Password\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IPAddress\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Username\"", sql, StringComparison.Ordinal);
    }
}

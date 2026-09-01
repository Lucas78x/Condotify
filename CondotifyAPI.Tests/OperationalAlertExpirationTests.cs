using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Observability;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class OperationalAlertExpirationTests
{
    [Theory]
    [InlineData("credentials")]
    [InlineData("temporary")]
    [InlineData("invites")]
    public void ExpirationQueries_AreTranslatedByPostgreSql(string queryType)
    {
        Environment.SetEnvironmentVariable(
            "CONDOTIFY_EQUIPMENT_SECRET",
            "condotify-tests-equipment-secret-2026");
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        using var context = new DatabaseContext(options);
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var query = queryType switch
        {
            "credentials" => OperationalAlertEvaluationService.ExpiringCredentialQuery(context, now, now.AddHours(72)),
            "temporary" => OperationalAlertEvaluationService.ExpiringTemporaryAccessQuery(context, now, now.AddHours(72)),
            _ => OperationalAlertEvaluationService.ExpiringRegistrationInviteQuery(context, now, now.AddHours(24))
        };
        var sql = query.IgnoreQueryFilters().ToQueryString();

        Assert.Contains("COUNT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExpiresAt", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0.5, "1 hora")]
    [InlineData(3.1, "4 horas")]
    [InlineData(25, "2 dias")]
    public void RemainingLabel_RoundsUpToAnOperationalWindow(double hours, string expected)
    {
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var label = OperationalAlertEvaluationService.RemainingLabel(now.AddHours(hours), now);

        Assert.Equal(expected, label);
    }
}

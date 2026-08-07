using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Resident;

namespace CondotifyAPI.Tests;

public sealed class ResourceDocumentNotificationTests
{
    private static readonly DateTime Now = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ResidentA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ResidentUnitLinkDTO Link(Guid residentId, bool isActive = true, DateTime? startsAt = null, DateTime? endsAt = null) => new()
    {
        Id = Guid.NewGuid(),
        ResidentId = residentId,
        UnitId = Guid.NewGuid(),
        IsActive = isActive,
        StartsAt = startsAt ?? Now.AddDays(-30),
        EndsAt = endsAt,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    [Fact]
    public void ResolveLicenseNotificationTargets_ReturnsResidentWithCurrentLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets([Link(ResidentA)], Now);

        Assert.Equal([ResidentA], result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesEndedLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, endsAt: Now.AddDays(-1))], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesInactiveLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, isActive: false)], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesNotYetStartedLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, startsAt: Now.AddDays(1))], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_DeduplicatesSameResidentInTwoUnits()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA), Link(ResidentA)], Now);

        Assert.Equal([ResidentA], result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ReturnsMultipleDistinctResidents()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA), Link(ResidentB)], Now);

        Assert.Equal(2, result.Count);
        Assert.Contains(ResidentA, result);
        Assert.Contains(ResidentB, result);
    }
}

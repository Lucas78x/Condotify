using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Models.Resident;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Covers <see cref="ResidentAuthController.Decide"/>, the pure "does this login succeed"
/// decision behind <c>POST /api/auth/resident/login</c>. CondotifyAPI.Tests has no EF
/// InMemory/SQLite provider (same constraint documented in
/// ResidentAuthorizationServiceTests), so the decision is extracted into an internal static
/// function (visible here via InternalsVisibleTo) exercised directly against hand-built DTOs
/// and a real <see cref="PasswordHasher{TUser}"/> - no DbContext involved.
///
/// The property that matters most: a non-existent email, a wrong password, an inactive
/// resident and a resident with no password set must all produce the exact same decision -
/// not merely "both fail", but the identical <see cref="ResidentAuthController.ResidentLoginDecision"/>
/// value, so nothing about *why* it failed leaks anywhere near the HTTP response.
/// </summary>
public class ResidentLoginTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PasswordHasher<ResidentAccess> Hasher = new();
    private const string CorrectPassword = "Abcdef1!";

    private static readonly string CorrectPasswordHash = Hasher.HashPassword(null!, CorrectPassword);

    private static UnitDTO Unit(Guid? licenseId = null, string number = "101") => new()
    {
        Id = Guid.NewGuid(),
        Number = number,
        Block = new BlockDTO
        {
            Id = Guid.NewGuid(),
            Name = "Bloco A",
            LicenseId = licenseId ?? Guid.NewGuid(),
        },
    };

    private static ResidentUnitLinkDTO Link(UnitDTO unit, bool isPrimary, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UnitId = unit.Id,
        Unit = unit,
        IsPrimary = isPrimary,
        IsActive = true,
        CreatedAt = createdAt ?? Now,
    };

    private static ResidentAccessDTO Resident(
        string? password = null,
        bool isActive = true,
        bool temporary = false,
        DateTime? expire = null,
        UnitDTO? unit = null,
        IEnumerable<ResidentUnitLinkDTO>? unitLinks = null)
    {
        var directUnit = unit ?? Unit();
        return new ResidentAccessDTO
        {
            Id = Guid.NewGuid(),
            Email = "morador@example.com",
            Password = password ?? CorrectPasswordHash,
            IsActive = isActive,
            Temporary = temporary,
            Expire = expire ?? Now.AddDays(1),
            UnitId = directUnit.Id,
            Unit = directUnit,
            UnitLinks = unitLinks?.ToList() ?? new List<ResidentUnitLinkDTO>(),
        };
    }

    [Fact]
    public void Decide_ValidCredential_Succeeds()
    {
        var licenseId = Guid.NewGuid();
        var unit = Unit(licenseId);
        var resident = Resident(unit: unit, unitLinks: new[] { Link(unit, isPrimary: true) });

        var decision = ResidentAuthController.Decide(resident, CorrectPassword, Hasher, Now);

        Assert.True(decision.Success);
        Assert.Equal(licenseId, decision.LicenseId);
    }

    [Fact]
    public void Decide_WrongPassword_Fails()
    {
        var resident = Resident();

        var decision = ResidentAuthController.Decide(resident, "TotallyWrongPassword1!", Hasher, Now);

        Assert.False(decision.Success);
        Assert.Null(decision.LicenseId);
    }

    [Fact]
    public void Decide_ResidentNotFound_FailsWithTheSameShapeAsWrongPassword()
    {
        var wrongPassword = ResidentAuthController.Decide(Resident(), "TotallyWrongPassword1!", Hasher, Now);
        var notFound = ResidentAuthController.Decide(null, CorrectPassword, Hasher, Now);

        Assert.False(notFound.Success);
        Assert.Equal(wrongPassword, notFound);
    }

    [Fact]
    public void Decide_InactiveResident_Fails()
    {
        var resident = Resident(isActive: false);

        var decision = ResidentAuthController.Decide(resident, CorrectPassword, Hasher, Now);

        Assert.False(decision.Success);
        Assert.Null(decision.LicenseId);
    }

    [Fact]
    public void Decide_EmptyStoredPassword_FailsWithoutThrowing()
    {
        var resident = Resident(password: "");

        var exception = Record.Exception(() => ResidentAuthController.Decide(resident, CorrectPassword, Hasher, Now));

        Assert.Null(exception);
        var decision = ResidentAuthController.Decide(resident, CorrectPassword, Hasher, Now);
        Assert.False(decision.Success);
    }

    [Fact]
    public void Decide_TemporaryResidentWithExpireInThePast_Fails()
    {
        var resident = Resident(temporary: true, expire: Now.AddMinutes(-1));

        var decision = ResidentAuthController.Decide(resident, CorrectPassword, Hasher, Now);

        Assert.False(decision.Success);
        Assert.Null(decision.LicenseId);
    }

    [Fact]
    public void Decide_AllFailureScenarios_ProduceTheIdenticalDecision()
    {
        var notFound = ResidentAuthController.Decide(null, CorrectPassword, Hasher, Now);
        var wrongPassword = ResidentAuthController.Decide(Resident(), "WrongPassword1!", Hasher, Now);
        var inactive = ResidentAuthController.Decide(Resident(isActive: false), CorrectPassword, Hasher, Now);
        var noPassword = ResidentAuthController.Decide(Resident(password: ""), CorrectPassword, Hasher, Now);
        var expired = ResidentAuthController.Decide(Resident(temporary: true, expire: Now.AddMinutes(-1)), CorrectPassword, Hasher, Now);

        Assert.Equal(notFound, wrongPassword);
        Assert.Equal(notFound, inactive);
        Assert.Equal(notFound, noPassword);
        Assert.Equal(notFound, expired);

        // Same reference to the shared failure singleton, not merely value-equal by luck.
        Assert.Same(ResidentAuthController.ResidentLoginDecision.Failure, notFound);
        Assert.Same(ResidentAuthController.ResidentLoginDecision.Failure, wrongPassword);
        Assert.Same(ResidentAuthController.ResidentLoginDecision.Failure, inactive);
        Assert.Same(ResidentAuthController.ResidentLoginDecision.Failure, noPassword);
        Assert.Same(ResidentAuthController.ResidentLoginDecision.Failure, expired);
    }

    // --- Licence resolution ------------------------------------------------------------

    [Fact]
    public void ResolveLicenseId_PrefersThePrimaryUnitLink()
    {
        var primaryLicenseId = Guid.NewGuid();
        var secondaryLicenseId = Guid.NewGuid();
        var primaryUnit = Unit(primaryLicenseId);
        var secondaryUnit = Unit(secondaryLicenseId);
        var resident = Resident(unit: primaryUnit, unitLinks: new[]
        {
            Link(secondaryUnit, isPrimary: false),
            Link(primaryUnit, isPrimary: true),
        });

        var licenseId = ResidentAuthController.ResolveLicenseId(resident);

        Assert.Equal(primaryLicenseId, licenseId);
    }

    [Fact]
    public void ResolveLicenseId_NoUnitLinks_FallsBackToTheDirectUnit()
    {
        // This is not a hypothetical: CondotifyAPI's own DevelopmentDataSeeder creates a
        // demo resident with a direct UnitId and zero ResidentUnitLinks rows.
        var licenseId = Guid.NewGuid();
        var directUnit = Unit(licenseId);
        var resident = Resident(unit: directUnit, unitLinks: Array.Empty<ResidentUnitLinkDTO>());

        var resolved = ResidentAuthController.ResolveLicenseId(resident);

        Assert.Equal(licenseId, resolved);
    }
}

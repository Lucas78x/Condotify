using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Resident;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace CondotifyAPI.Tests;

public sealed class ResidentContextTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PasswordHasher<ResidentAccess> Hasher = new();

    [Fact]
    public void ResolveContexts_GroupsMultipleUnitsAndCondominiumsUnderOneAccount()
    {
        var first = License("Residencial Azul");
        var second = License("Residencial Verde");
        var resident = Resident(
            Link(Unit(first, "A", "101"), true, ResidentUnitRelationshipEnum.OwnerResponsible),
            Link(Unit(first, "B", "202"), false, ResidentUnitRelationshipEnum.TenantResponsible),
            Link(Unit(second, "Torre 1", "35"), false, ResidentUnitRelationshipEnum.OwnerResponsible));

        var contexts = ResidentAuthController.ResolveContexts(resident, Now);

        Assert.Equal(2, contexts.Count);
        Assert.Equal(2, contexts.Single(x => x.LicenseId == first.Id).Units.Count);
        Assert.Single(contexts.Single(x => x.LicenseId == second.Id).Units);
    }

    [Fact]
    public void ResolveContexts_ExcludesInactiveFutureAndExpiredLinks()
    {
        var license = License("Residencial Azul");
        var active = Link(Unit(license, "A", "101"), true, ResidentUnitRelationshipEnum.Resident);
        var inactive = Link(Unit(license, "A", "102"), false, ResidentUnitRelationshipEnum.Resident);
        inactive.IsActive = false;
        var future = Link(Unit(license, "A", "103"), false, ResidentUnitRelationshipEnum.Resident);
        future.StartsAt = Now.AddDays(1);
        var expired = Link(Unit(license, "A", "104"), false, ResidentUnitRelationshipEnum.Resident);
        expired.EndsAt = Now.AddMinutes(-1);

        var context = Assert.Single(ResidentAuthController.ResolveContexts(Resident(active, inactive, future, expired), Now));

        Assert.Equal("101", Assert.Single(context.Units).UnitNumber);
    }

    [Fact]
    public void Decide_AcceptsAnActiveRequestedCondominiumAndRejectsAnUnlinkedOne()
    {
        const string password = "Abcdef1!";
        var first = License("Residencial Azul");
        var second = License("Residencial Verde");
        var resident = Resident(
            Link(Unit(first, "A", "101"), true, ResidentUnitRelationshipEnum.OwnerResponsible),
            Link(Unit(second, "B", "202"), false, ResidentUnitRelationshipEnum.OwnerResponsible));
        resident.Password = Hasher.HashPassword(null!, password);

        var allowed = ResidentAuthController.Decide(resident, password, Hasher, Now, second.Id);
        var denied = ResidentAuthController.Decide(resident, password, Hasher, Now, Guid.NewGuid());

        Assert.True(allowed.Success);
        Assert.Equal(second.Id, allowed.LicenseId);
        Assert.False(denied.Success);
    }

    [Fact]
    public void Controller_DoesNotMakeAuthenticatedResidentActionsAnonymous()
    {
        Assert.Null(typeof(ResidentAuthController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        foreach (var methodName in new[] { nameof(ResidentAuthController.Contexts), nameof(ResidentAuthController.SwitchContext), nameof(ResidentAuthController.ChangePassword) })
        {
            var method = typeof(ResidentAuthController).GetMethod(methodName)!;
            Assert.NotNull(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
            Assert.Null(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        }
    }

    private static ResidentAccessDTO Resident(params ResidentUnitLinkDTO[] links)
    {
        var primary = links.First(x => x.IsPrimary);
        var resident = new ResidentAccessDTO
        {
            Id = Guid.NewGuid(), Name = "Lucas", Email = "lucas@example.com", IsActive = true,
            UnitId = primary.UnitId, Unit = primary.Unit, UnitLinks = links
        };
        foreach (var link in links) { link.ResidentId = resident.Id; link.Resident = resident; }
        return resident;
    }

    private static LicenseDTO License(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static UnitDTO Unit(LicenseDTO license, string blockName, string number)
    {
        var block = new BlockDTO { Id = Guid.NewGuid(), Name = blockName, LicenseId = license.Id, License = license };
        return new UnitDTO { Id = Guid.NewGuid(), Number = number, BlockId = block.Id, Block = block };
    }

    private static ResidentUnitLinkDTO Link(UnitDTO unit, bool primary, ResidentUnitRelationshipEnum relationship) => new()
    {
        Id = Guid.NewGuid(), UnitId = unit.Id, Unit = unit, Relationship = relationship,
        IsPrimary = primary, IsActive = true, StartsAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1), UpdatedAt = Now
    };
}

using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Services.AccessControl;

namespace CondotifyAPI.Tests;

public class AccessRouteResolverTests
{
    [Fact]
    public void ResolveAudience_ShouldPrioritizeTemporaryCategories()
    {
        Assert.Equal(AccessRouteAudienceEnum.Visitor, AccessRouteResolver.ResolveAudience(Person(ResidentAccessTypeEnum.Guest)));
        Assert.Equal(AccessRouteAudienceEnum.ServiceProvider, AccessRouteResolver.ResolveAudience(Person(ResidentAccessTypeEnum.ServiceProvider)));
    }

    [Theory]
    [InlineData(ResidentUnitRelationshipEnum.OwnerResponsible, AccessRouteAudienceEnum.OwnerResponsible)]
    [InlineData(ResidentUnitRelationshipEnum.TenantResponsible, AccessRouteAudienceEnum.TenantResponsible)]
    [InlineData(ResidentUnitRelationshipEnum.Responsible, AccessRouteAudienceEnum.Responsible)]
    [InlineData(ResidentUnitRelationshipEnum.Dependent, AccessRouteAudienceEnum.Dependent)]
    [InlineData(ResidentUnitRelationshipEnum.Resident, AccessRouteAudienceEnum.Resident)]
    public void ResolveAudience_ShouldUsePrimaryUnitRelationship(
        ResidentUnitRelationshipEnum relationship,
        AccessRouteAudienceEnum expected)
    {
        Assert.Equal(expected, AccessRouteResolver.ResolveAudience(Person(ResidentAccessTypeEnum.NonResponsible, relationship)));
    }

    [Fact]
    public void ResolveAudience_IgnoresAnInactivePrimaryLink()
    {
        var person = Person(ResidentAccessTypeEnum.NonResponsible, ResidentUnitRelationshipEnum.Dependent);
        person.UnitLinks.Add(new ResidentUnitLinkDTO
        {
            Relationship = ResidentUnitRelationshipEnum.OwnerResponsible,
            IsPrimary = true,
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        Assert.Equal(AccessRouteAudienceEnum.Dependent, AccessRouteResolver.ResolveAudience(person));
    }

    [Fact]
    public void ResolveAudience_UsesTheRelationshipFromTheRequestedLicense()
    {
        var requestedLicense = Guid.NewGuid();
        var otherLicense = Guid.NewGuid();
        var person = new ResidentAccessDTO
        {
            AccessType = ResidentAccessTypeEnum.NonResponsible,
            UnitLinks =
            [
                Link(otherLicense, ResidentUnitRelationshipEnum.OwnerResponsible, primary: true),
                Link(requestedLicense, ResidentUnitRelationshipEnum.Dependent)
            ]
        };

        Assert.Equal(AccessRouteAudienceEnum.Dependent, AccessRouteResolver.ResolveAudience(person, requestedLicense));
    }

    private static ResidentAccessDTO Person(
        ResidentAccessTypeEnum type,
        ResidentUnitRelationshipEnum relationship = ResidentUnitRelationshipEnum.Resident) => new()
    {
        AccessType = type,
        UnitLinks = new List<ResidentUnitLinkDTO>
        {
            new() { Relationship = relationship, IsPrimary = true, IsActive = true }
        }
    };

    private static ResidentUnitLinkDTO Link(Guid licenseId, ResidentUnitRelationshipEnum relationship, bool primary = false)
    {
        var block = new BlockDTO { Id = Guid.NewGuid(), LicenseId = licenseId };
        var unit = new UnitDTO { Id = Guid.NewGuid(), BlockId = block.Id, Block = block };
        return new ResidentUnitLinkDTO
        {
            UnitId = unit.Id,
            Unit = unit,
            Relationship = relationship,
            IsPrimary = primary,
            IsActive = true
        };
    }
}

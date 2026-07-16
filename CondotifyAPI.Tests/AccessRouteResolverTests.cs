using CondotifyAPI.Domain.DTO.Resident;
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
}

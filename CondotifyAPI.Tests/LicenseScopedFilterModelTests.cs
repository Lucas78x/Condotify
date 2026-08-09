using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class LicenseScopedFilterModelTests
{
    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;

        return new DatabaseContext(options);
    }

    [Theory]
    [InlineData(typeof(AccessRouteDTO))]
    [InlineData(typeof(AmenityDTO))]
    [InlineData(typeof(AmenityBookingDTO))]
    [InlineData(typeof(DeliveryDTO))]
    [InlineData(typeof(TicketDTO))]
    [InlineData(typeof(LicenseCredentialPolicyDTO))]
    [InlineData(typeof(LicenseUserAccessDTO))]
    [InlineData(typeof(IncidentDTO))]
    [InlineData(typeof(AutomationRuleDTO))]
    [InlineData(typeof(EmergencySessionDTO))]
    [InlineData(typeof(DigitalPassDTO))]
    public void LicenseScopedEntities_HaveAQueryFilterRegistered(Type entityClrType)
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(entityClrType);

        Assert.NotNull(entityType);
        Assert.True(typeof(ILicenseScoped).IsAssignableFrom(entityClrType), $"{entityClrType.Name} deveria implementar ILicenseScoped.");
        Assert.NotNull(entityType!.GetQueryFilter());
    }

    [Fact]
    public void AllILicenseScopedEntities_HaveAQueryFilterRegistered()
    {
        using var context = CreateContext();

        var licenseScopedTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(ILicenseScoped).IsAssignableFrom(x.ClrType))
            .ToList();

        Assert.Equal(29, licenseScopedTypes.Count);
        Assert.All(licenseScopedTypes, entityType => Assert.NotNull(entityType.GetQueryFilter()));
    }
}

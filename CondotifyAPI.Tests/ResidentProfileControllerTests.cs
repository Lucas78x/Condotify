using CondotifyAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Tests;

public sealed class ResidentProfileControllerTests
{
    [Fact]
    public void CreateVisitInput_NormalizesOptionalNulls()
    {
        var input = new CondotifyAPI.Data.Login.CreateResidentVisitIn
        {
            VisitorName = "  Visitante  ",
            Document = null!,
            PhoneNumber = null!,
            Company = null!,
            Purpose = null!,
            VehiclePlate = null!,
            IdempotencyKey = null!,
            RouteIds = null!
        };

        Assert.Null(ResidentProfileController.NormalizeAndValidateCreateVisitInput(input));
        Assert.Equal("Visitante", input.VisitorName);
        Assert.Empty(input.Document);
        Assert.Empty(input.RouteIds);
    }

    [Theory]
    [InlineData(151, "nome")]
    [InlineData(201, "motivo")]
    public void CreateVisitInput_RejectsOversizedValues(int length, string field)
    {
        var input = new CondotifyAPI.Data.Login.CreateResidentVisitIn { VisitorName = "Visitante" };
        if (field == "nome") input.VisitorName = new string('a', length);
        else input.Purpose = new string('a', length);

        Assert.NotNull(ResidentProfileController.NormalizeAndValidateCreateVisitInput(input));
    }

    [Fact]
    public void Controller_RequiresTheResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentProfileController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Resident", authorize.Policy);
        Assert.Empty(typeof(ResidentProfileController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void Me_DoesNotOverrideAuthorizationWithAllowAnonymous()
    {
        var action = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.Me));

        Assert.NotNull(action);
        Assert.Empty(action!.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(ResidentProfileController.CreateVisit))]
    [InlineData(nameof(ResidentProfileController.AmenityAvailability))]
    [InlineData(nameof(ResidentProfileController.CreateBooking))]
    [InlineData(nameof(ResidentProfileController.CancelBooking))]
    public void ResidentCommands_DoNotOverrideTheResidentPolicy(string actionName)
    {
        var action = typeof(ResidentProfileController).GetMethods()
            .Single(x => x.Name == actionName);

        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public void ResidentCommandRoutes_UseTheExpectedHttpVerbs()
    {
        var createVisit = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.CreateVisit));
        var availability = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.AmenityAvailability));
        var createBooking = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.CreateBooking));
        var cancelBooking = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.CancelBooking));

        Assert.Equal("visits", Assert.Single(createVisit!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Equal("amenities/{amenityId:guid}/availability", Assert.Single(availability!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        Assert.Equal("amenities/{amenityId:guid}/bookings", Assert.Single(createBooking!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Equal("bookings/{bookingId:guid}", Assert.Single(cancelBooking!.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Cast<HttpDeleteAttribute>()).Template);
    }

    [Theory]
    [InlineData("morador@example.com", true)]
    [InlineData("  morador@example.com  ", true)]
    [InlineData("Morador <morador@example.com>", false)]
    [InlineData("invalido", false)]
    public void RegistrationInvite_AcceptsOnlyPlainEmailAddress(string email, bool expected) =>
        Assert.Equal(expected, ResidentProfileController.IsValidEmail(email));

    [Fact]
    public void RegistrationInvite_UsesAuthenticatedResidentRoute()
    {
        var action = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.CreateRegistrationInvite));

        Assert.NotNull(action);
        Assert.Equal("registration-invites", Assert.Single(action!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(ResidentUnitRelationshipEnum.OwnerResponsible, true)]
    [InlineData(ResidentUnitRelationshipEnum.TenantResponsible, true)]
    [InlineData(ResidentUnitRelationshipEnum.Responsible, true)]
    [InlineData(ResidentUnitRelationshipEnum.Resident, false)]
    [InlineData(ResidentUnitRelationshipEnum.Dependent, false)]
    public void RegistrationInvite_OnlyResponsibleRelationshipsCanInvite(
        ResidentUnitRelationshipEnum relationship,
        bool expected) =>
        Assert.Equal(expected, ResidentProfileController.CanInviteFromRelationship(relationship));

    [Fact]
    public void Deliveries_UsesResidentPolicyAndResidentRoute()
    {
        var action = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.Deliveries));

        Assert.NotNull(action);
        Assert.Equal("deliveries", Assert.Single(action!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void ResidentMediaRoute_UsesResidentPolicyAndExpectedTemplate()
    {
        var action = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.Media));

        Assert.NotNull(action);
        Assert.Equal("media/{mediaId:guid}", Assert.Single(action!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>()).Template);
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public void ResidentMediaFilters_RejectOtherResidentsLicensesAndUnits()
    {
        var residentId = Guid.NewGuid();
        var licenseId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        const string reference = "/private-media/license/media";
        var grant = new ResidentAccessGrant(
            residentId,
            licenseId,
            [unitId],
            ResidentAccessTypeEnum.Default,
            false);

        var ownPhoto = ResidentProfileController.OwnMediaFilter(grant, reference).Compile();
        Assert.True(ownPhoto(new ResidentAccessDTO { Id = residentId, ImgUrl = reference }));
        Assert.False(ownPhoto(new ResidentAccessDTO { Id = Guid.NewGuid(), ImgUrl = reference }));

        var hostedVisit = ResidentProfileController.HostedVisitMediaFilter(grant, reference).Compile();
        Assert.True(hostedVisit(new AccessVisitDTO { LicenseId = licenseId, HostResidentId = residentId, PhotoUrl = reference }));
        Assert.False(hostedVisit(new AccessVisitDTO { LicenseId = Guid.NewGuid(), HostResidentId = residentId, PhotoUrl = reference }));
        Assert.False(hostedVisit(new AccessVisitDTO { LicenseId = licenseId, HostResidentId = Guid.NewGuid(), PhotoUrl = reference }));

        var delivery = ResidentProfileController.DeliveryMediaFilter(grant, reference).Compile();
        Assert.True(delivery(new DeliveryDTO { LicenseId = licenseId, RecipientResidentId = residentId, UnitId = unitId, PhotoUrl = reference }));
        Assert.False(delivery(new DeliveryDTO { LicenseId = licenseId, RecipientResidentId = residentId, UnitId = Guid.NewGuid(), PhotoUrl = reference }));
        Assert.False(delivery(new DeliveryDTO { LicenseId = licenseId, RecipientResidentId = Guid.NewGuid(), UnitId = unitId, PhotoUrl = reference }));
    }

    [Theory]
    [InlineData(nameof(ResidentProfileController.IssuePass))]
    [InlineData(nameof(ResidentProfileController.RevokePass))]
    public void DigitalPassCommands_DoNotOverrideTheResidentPolicy(string actionName)
    {
        var action = typeof(ResidentProfileController).GetMethods()
            .Single(x => x.Name == actionName);

        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public void DigitalPassRoutes_UseExpectedVerbsAndTemplate()
    {
        var issue = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.IssuePass));
        var revoke = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.RevokePass));

        Assert.Equal("visits/{visitId:guid}/pass", Assert.Single(issue!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Equal("visits/{visitId:guid}/pass", Assert.Single(revoke!.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Cast<HttpDeleteAttribute>()).Template);
    }

    [Fact]
    public void ResidentCftvController_RequiresResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentCftvController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        var route = Assert.Single(typeof(ResidentCftvController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());

        Assert.Equal("Resident", authorize.Policy);
        Assert.Equal("api/resident/cameras", route.Template);
        Assert.Empty(typeof(ResidentCftvController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(ResidentCftvController.GetCameras), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResidentCftvController.Snapshot), typeof(HttpGetAttribute), "{deviceId:guid}/snapshot")]
    [InlineData(nameof(ResidentCftvController.OpenSession), typeof(HttpPostAttribute), "{deviceId:guid}/sessions")]
    [InlineData(nameof(ResidentCftvController.CloseSession), typeof(HttpDeleteAttribute), "{deviceId:guid}/sessions/{channel:int}")]
    public void ResidentCftvRoutes_UseExpectedVerbs(string actionName, Type attributeType, string? template)
    {
        var action = typeof(ResidentCftvController).GetMethod(actionName);
        var attribute = Assert.Single(action!.GetCustomAttributes(attributeType, false).Cast<HttpMethodAttribute>());

        Assert.Equal(template, attribute.Template);
        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }
}

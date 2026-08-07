using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResidentBoletosControllerTests
{
    [Fact]
    public void Controller_RequiresTheResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentBoletosController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Resident", authorize.Policy);
        Assert.Empty(typeof(ResidentBoletosController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(ResidentBoletosController.List), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResidentBoletosController.Download), typeof(HttpGetAttribute), "{documentId:guid}/file")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string? route)
    {
        var method = typeof(ResidentBoletosController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    private static readonly Guid LicenseId = Guid.NewGuid();
    private static readonly Guid OtherLicenseId = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();
    private static readonly Guid OtherUnitId = Guid.NewGuid();

    private static ResidentAccessGrant Grant() => new(
        Guid.NewGuid(),
        LicenseId,
        new[] { UnitId },
        ResidentAccessTypeEnum.Responsible,
        true);

    private static BoletoDocumentDTO Document(
        BoletoBatchStatusEnum status = BoletoBatchStatusEnum.Published,
        Guid? licenseId = null,
        Guid? unitId = null,
        bool hasUnit = true) => new()
        {
            Id = Guid.NewGuid(),
            UnitId = hasUnit ? unitId ?? UnitId : null,
            Batch = new BoletoBatchDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId ?? LicenseId,
                Status = status
            }
        };

    [Fact]
    public void IsVisibleTo_MatchesWhenPublishedSameLicenseAndUnitInGrant()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.True(predicate(Document()));
    }

    [Fact]
    public void IsVisibleTo_RejectsDocumentFromAnotherLicense()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.False(predicate(Document(licenseId: OtherLicenseId)));
    }

    [Fact]
    public void IsVisibleTo_RejectsUnitNotInGrant()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.False(predicate(Document(unitId: OtherUnitId)));
    }

    [Fact]
    public void IsVisibleTo_RejectsUnpublishedBatch()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.False(predicate(Document(status: BoletoBatchStatusEnum.PendingReview)));
    }

    [Fact]
    public void IsVisibleTo_RejectsCancelledBatch()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.False(predicate(Document(status: BoletoBatchStatusEnum.Cancelled)));
    }

    [Fact]
    public void IsVisibleTo_RejectsDocumentWithNoUnitAssigned()
    {
        var predicate = ResidentBoletosController.IsVisibleTo(Grant()).Compile();

        Assert.False(predicate(Document(hasUnit: false)));
    }
}

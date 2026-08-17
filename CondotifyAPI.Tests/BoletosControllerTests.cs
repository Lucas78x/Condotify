using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class BoletosControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var authorize = typeof(BoletosController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.Single(authorize);
    }

    [Theory]
    [InlineData(nameof(BoletosController.UploadBatch), typeof(HttpPostAttribute), "batches", LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(BoletosController.UploadSingle), typeof(HttpPostAttribute), "single", LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(BoletosController.ListBatches), typeof(HttpGetAttribute), "batches", LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(BoletosController.GetBatch), typeof(HttpGetAttribute), "batches/{batchId:guid}", LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(BoletosController.GetDocumentFile), typeof(HttpGetAttribute), "documents/{documentId:guid}/file", LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(BoletosController.UpdateDocument), typeof(HttpPutAttribute), "documents/{documentId:guid}", LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(BoletosController.Publish), typeof(HttpPostAttribute), "batches/{batchId:guid}/publish", LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(BoletosController.Cancel), typeof(HttpPostAttribute), "batches/{batchId:guid}/cancel", LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(BoletosController.DeleteDocument), typeof(HttpDeleteAttribute), "documents/{documentId:guid}", LicensePermissionEnum.ManageFinance)]
    public void Actions_UseExpectedRouteVerbAndPermission(string actionName, Type httpAttributeType, string route, LicensePermissionEnum expectedPermission)
    {
        var method = typeof(BoletosController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);

        var permission = Assert.Single(method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(expectedPermission, Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }

    private static readonly DateTime Now = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    private static BoletoDocumentDTO DocumentWithLinks(Guid documentId, params ResidentUnitLinkDTO[] links) => new()
    {
        Id = documentId,
        UnitId = Guid.NewGuid(),
        Unit = new UnitDTO { ResidentLinks = [.. links] }
    };

    private static ResidentUnitLinkDTO Link(Guid residentId, bool isActive = true, DateTime? startsAt = null, DateTime? endsAt = null) => new()
    {
        Id = Guid.NewGuid(),
        ResidentId = residentId,
        IsActive = isActive,
        StartsAt = startsAt ?? Now.AddYears(-1),
        EndsAt = endsAt
    };

    [Fact]
    public void ResolveNotificationTargets_CurrentResident_IsNotifiedWithDocumentScopedKey()
    {
        var residentId = Guid.NewGuid();
        var documentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var document = DocumentWithLinks(documentId, Link(residentId));

        var targets = BoletosController.ResolveNotificationTargets(document, Now).ToList();

        var target = Assert.Single(targets);
        Assert.Equal(residentId, target.ResidentId);
        Assert.Equal($"boleto-published:{documentId:N}", target.DeduplicationKey);
        Assert.DoesNotContain("-", target.DeduplicationKey["boleto-published:".Length..]);
    }

    [Fact]
    public void ResolveNotificationTargets_LinkActiveButAlreadyEnded_IsExcluded()
    {
        // O morador mudou de unidade: o vinculo continua IsActive mas EndsAt ja passou.
        // Notifica-lo seria push de um boleto que ele nem consegue abrir.
        var moved = Link(Guid.NewGuid(), endsAt: Now.AddDays(-1));
        var current = Link(Guid.NewGuid());
        var document = DocumentWithLinks(Guid.NewGuid(), moved, current);

        var targets = BoletosController.ResolveNotificationTargets(document, Now).ToList();

        var target = Assert.Single(targets);
        Assert.Equal(current.ResidentId, target.ResidentId);
    }

    [Fact]
    public void ResolveNotificationTargets_InactiveOrNotYetStartedLink_IsExcluded()
    {
        var document = DocumentWithLinks(
            Guid.NewGuid(),
            Link(Guid.NewGuid(), isActive: false),
            Link(Guid.NewGuid(), startsAt: Now.AddDays(1)));

        Assert.Empty(BoletosController.ResolveNotificationTargets(document, Now));
    }

    [Fact]
    public void ResolveNotificationTargets_UnitWithoutCurrentResidents_ReturnsEmpty()
    {
        var document = DocumentWithLinks(Guid.NewGuid());

        Assert.Empty(BoletosController.ResolveNotificationTargets(document, Now));
    }

    [Fact]
    public void ResolveNotificationTargets_DocumentWithoutUnit_ReturnsEmpty()
    {
        var document = new BoletoDocumentDTO { Id = Guid.NewGuid(), UnitId = null, Unit = null };

        Assert.Empty(BoletosController.ResolveNotificationTargets(document, Now));
    }

    [Fact]
    public void ResolveNotificationTargets_SameResidentLinkedTwice_IsNotifiedOnce()
    {
        var residentId = Guid.NewGuid();
        var document = DocumentWithLinks(Guid.NewGuid(), Link(residentId), Link(residentId));

        Assert.Single(BoletosController.ResolveNotificationTargets(document, Now));
    }

    [Fact]
    public void ResolveNotificationTargets_DeduplicationKeyIsPerDocument()
    {
        var residentId = Guid.NewGuid();
        var first = DocumentWithLinks(Guid.NewGuid(), Link(residentId));
        var second = DocumentWithLinks(Guid.NewGuid(), Link(residentId));

        var firstKey = BoletosController.ResolveNotificationTargets(first, Now).Single().DeduplicationKey;
        var secondKey = BoletosController.ResolveNotificationTargets(second, Now).Single().DeduplicationKey;

        Assert.NotEqual(firstKey, secondKey);
    }

    [Theory]
    // Kind=Unspecified e o que o model binder produz para a data escolhida no
    // MudDatePicker do portal (o cliente serializa "O" sem offset); Npgsql recusa
    // qualquer Kind != Utc numa coluna 'timestamp with time zone'.
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void AsUtcDate_AlwaysProducesUtcMidnightWithoutShiftingTheDay(DateTimeKind kind)
    {
        var picked = DateTime.SpecifyKind(new DateTime(2026, 8, 10, 15, 42, 17), kind);

        var normalized = BoletosController.AsUtcDate(picked);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), normalized);
    }

    [Theory]
    [InlineData("123.456.789-01", "12345678901")]
    [InlineData("12345678901", "12345678901")]
    [InlineData("  123.456.789-01 ", "12345678901")]
    [InlineData("", "")]
    public void DigitsOnly_StripsCpfFormatting(string stored, string expected) =>
        Assert.Equal(expected, BoletosController.DigitsOnly(stored));
}

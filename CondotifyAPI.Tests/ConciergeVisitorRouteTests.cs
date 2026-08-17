using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Infrastructure;
using Condotify.Models;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace CondotifyAPI.Tests;

public sealed class ConciergeVisitorRouteTests
{
    [Fact]
    public void VisitorRouteProjection_ShouldNotLoadEquipmentPassword()
    {
        Environment.SetEnvironmentVariable(
            "CONDOTIFY_EQUIPMENT_SECRET",
            "condotify-tests-equipment-secret-2026");

        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;

        using var context = new DatabaseContext(options);

        var sql = ConciergeController
            .VisitorRouteQuery(context, Guid.NewGuid())
            .IgnoreQueryFilters()
            .ToQueryString();

        Assert.DoesNotContain("\"Password\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"Type\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateVisitInput_ShouldNormalizeOptionalNulls()
    {
        var input = new CreateConciergeVisitIn
        {
            VisitorName = "  Visitante Teste  ",
            Document = null!,
            PhoneNumber = null!,
            Company = null!,
            Purpose = null!,
            VehiclePlate = null!,
            ImageBase64 = null!,
            IdempotencyKey = null!,
            RouteIds = null!
        };

        ConciergeController.NormalizeCreateVisitInput(input);

        Assert.Equal("Visitante Teste", input.VisitorName);
        Assert.Equal(string.Empty, input.Document);
        Assert.Equal(string.Empty, input.PhoneNumber);
        Assert.Equal(string.Empty, input.Company);
        Assert.Equal(string.Empty, input.Purpose);
        Assert.Equal(string.Empty, input.VehiclePlate);
        Assert.Empty(input.RouteIds);
        Assert.Null(ConciergeController.ValidateCreateVisitInput(input));
    }

    [Theory]
    [InlineData(nameof(CreateConciergeVisitIn.VisitorName), 151, "nome")]
    [InlineData(nameof(CreateConciergeVisitIn.Document), 15, "documento")]
    [InlineData(nameof(CreateConciergeVisitIn.PhoneNumber), 21, "telefone")]
    [InlineData(nameof(CreateConciergeVisitIn.Company), 151, "empresa")]
    [InlineData(nameof(CreateConciergeVisitIn.Purpose), 201, "motivo")]
    [InlineData(nameof(CreateConciergeVisitIn.VehiclePlate), 21, "placa")]
    [InlineData(nameof(CreateConciergeVisitIn.IdempotencyKey), 141, "idempotência")]
    public void CreateVisitInput_ShouldRejectValuesThatExceedPersistenceLimits(
        string property,
        int length,
        string expectedMessage)
    {
        var input = new CreateConciergeVisitIn { VisitorName = "Visitante" };
        typeof(CreateConciergeVisitIn).GetProperty(property)!.SetValue(input, new string('X', length));

        ConciergeController.NormalizeCreateVisitInput(input);
        var error = ConciergeController.ValidateCreateVisitInput(input);

        Assert.Contains(expectedMessage, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConciergeVisitForm_ShouldAcceptACompleteQrVisit()
    {
        var form = new ConciergeVisitFormViewModel
        {
            HostResidentId = Guid.NewGuid(),
            VisitorName = "Visitante de validação",
            CredentialType = 2,
            ValidFrom = DateTime.Now,
            ValidTo = DateTime.Now.AddHours(4),
            MaxUses = 1,
            RouteIds = [Guid.NewGuid()]
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            form,
            new ValidationContext(form),
            results,
            validateAllProperties: true);

        Assert.True(valid, string.Join("; ", results.Select(x => x.ErrorMessage)));
    }
}

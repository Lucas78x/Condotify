using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Structure;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Tests;

public sealed class PersonRegistrationValidationTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ResidentAccessTypeEnum.Guest)]
    [InlineData(ResidentAccessTypeEnum.ServiceProvider)]
    public void TemporaryCategories_RequireTemporaryAccess(ResidentAccessTypeEnum accessType)
    {
        var input = Valid(accessType);
        input.Temporary = false;

        var error = LicenseStructureController.ValidateResidentRegistration(input, Now);

        Assert.Equal("Visitantes e prestadores devem possuir acesso temporario.", error);
    }

    [Theory]
    [InlineData(ResidentAccessTypeEnum.Guest)]
    [InlineData(ResidentAccessTypeEnum.ServiceProvider)]
    public void TemporaryCategories_RequireFutureExpiration(ResidentAccessTypeEnum accessType)
    {
        var input = Valid(accessType);
        input.Expire = Now;

        var error = LicenseStructureController.ValidateResidentRegistration(input, Now);

        Assert.Equal("Informe uma validade futura para o acesso temporario.", error);
    }

    [Theory]
    [InlineData(ResidentAccessTypeEnum.Guest)]
    [InlineData(ResidentAccessTypeEnum.ServiceProvider)]
    public void TemporaryCategories_CannotUseResidentialRelationship(ResidentAccessTypeEnum accessType)
    {
        var input = Valid(accessType);
        input.Relationship = ResidentUnitRelationshipEnum.OwnerResponsible;

        var error = LicenseStructureController.ValidateResidentRegistration(input, Now);

        Assert.Equal("Visitantes e prestadores nao podem usar vinculo residencial.", error);
    }

    [Fact]
    public void ResponsibleResident_RequiresResponsibleRelationship()
    {
        var input = Valid(ResidentAccessTypeEnum.Responsible);
        input.Relationship = ResidentUnitRelationshipEnum.Dependent;

        var error = LicenseStructureController.ValidateResidentRegistration(input, Now);

        Assert.Equal("Selecione um vinculo de responsavel para este morador.", error);
    }

    [Fact]
    public void NonResponsibleResident_AcceptsDependentRelationship()
    {
        var input = Valid(ResidentAccessTypeEnum.NonResponsible);
        input.Relationship = ResidentUnitRelationshipEnum.Dependent;

        var error = LicenseStructureController.ValidateResidentRegistration(input, Now);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(ResidentAccessTypeEnum.Guest)]
    [InlineData(ResidentAccessTypeEnum.ServiceProvider)]
    public void ValidTemporaryCategory_IsAccepted(ResidentAccessTypeEnum accessType) =>
        Assert.Null(LicenseStructureController.ValidateResidentRegistration(Valid(accessType), Now));

    private static CreateResidentIn Valid(ResidentAccessTypeEnum accessType) => new()
    {
        UnitId = Guid.NewGuid(),
        Name = "Pessoa de teste",
        AccessType = accessType,
        Relationship = accessType == ResidentAccessTypeEnum.Responsible
            ? ResidentUnitRelationshipEnum.OwnerResponsible
            : ResidentUnitRelationshipEnum.Resident,
        Temporary = accessType is ResidentAccessTypeEnum.Guest or ResidentAccessTypeEnum.ServiceProvider,
        Expire = Now.AddDays(1)
    };
}

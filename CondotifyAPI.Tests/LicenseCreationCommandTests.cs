using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Domain.Models;

namespace CondotifyAPI.Tests;

public sealed class LicenseCreationCommandTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Constructor_NormalizesExpirationToUtc(DateTimeKind kind)
    {
        var supplied = DateTime.SpecifyKind(new DateTime(2030, 8, 18, 12, 30, 0), kind);

        var command = new CreateLicenseByEnterpriseCommand(
            Guid.NewGuid(),
            "Condominio Teste",
            "12345678000190",
            "Salvador",
            "Brasil",
            "TESTE-01",
            OrganizationTypeEnum.Residential,
            BuildingTypeEnum.Vertical,
            LicenseTypeEnum.Full,
            new Location(),
            supplied);

        var expected = kind switch
        {
            DateTimeKind.Utc => supplied,
            DateTimeKind.Local => supplied.ToUniversalTime(),
            _ => DateTime.SpecifyKind(supplied, DateTimeKind.Utc)
        };

        Assert.Equal(DateTimeKind.Utc, command.ExpireDate.Kind);
        Assert.Equal(expected, command.ExpireDate);
    }
}

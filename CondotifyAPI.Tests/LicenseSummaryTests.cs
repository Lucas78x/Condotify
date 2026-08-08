using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Query;

namespace CondotifyAPI.Tests;

public sealed class LicenseSummaryTests
{
    [Fact]
    public void Summary_ShouldMapLocationWithoutUsingCnpj()
    {
        var license = new License
        {
            Name = "Condomínio Teste",
            Code = "TEST-01",
            CNPJ = "00.000.000/0001-00",
            City = "Camaçari",
            Country = "Brasil",
            Blocks = []
        };

        var summary = GetLicenseSummariesByUserQueryHandler.ToSummary(license);

        Assert.Equal("Camaçari", summary.Cidade);
        Assert.Equal("Brasil", summary.Estado);
        Assert.NotEqual(license.CNPJ, summary.Cidade);
    }

    [Fact]
    public void Summary_ShouldExposeEnabledModulesFromDomain()
    {
        var license = new License
        {
            Name = "Condomínio com módulos",
            Code = "TEST-02",
            City = "Salvador",
            Country = "Brasil",
            Blocks = [],
            EnabledModules = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings
        };

        var summary = GetLicenseSummariesByUserQueryHandler.ToSummary(license);

        Assert.Equal((long)(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings), summary.EnabledModules);
    }
}

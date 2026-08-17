using System.IdentityModel.Tokens.Jwt;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CondotifyAPI.Tests;

public class JwtPrincipalTypeTests
{
    private static readonly Guid LicenseId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly PasswordHasher<UserAccess> Hasher = new();

    [Fact]
    public void StaffToken_CarriesPrincipalTypeUser()
    {
        var token = ReadToken(CreateService().CreateAccessToken(SampleUser()));

        Assert.Equal(PrincipalTypes.User, token.Claims.First(x => x.Type == PrincipalTypes.Claim).Value);
    }

    [Fact]
    public void StaffToken_CarriesFirstAccessRequirement()
    {
        var token = ReadToken(CreateService().CreateAccessToken(SampleUser(firstAccess: true)));

        Assert.Equal("true", token.Claims.First(x => x.Type == "first_access").Value);
    }

    [Fact]
    public void ResidentToken_CarriesPrincipalTypeResident()
    {
        var token = ReadToken(CreateService().CreateResidentAccessToken(SampleResident(), LicenseId));

        Assert.Equal(PrincipalTypes.Resident, token.Claims.First(x => x.Type == PrincipalTypes.Claim).Value);
    }

    [Fact]
    public void ResidentToken_CarriesTheLicenseButNoUnitList()
    {
        var token = ReadToken(CreateService().CreateResidentAccessToken(SampleResident(), LicenseId));

        Assert.Equal(LicenseId.ToString(), token.Claims.First(x => x.Type == "license_id").Value);
        Assert.DoesNotContain(token.Claims, x => x.Type is "unit_id" or "unit_ids" or "units");
    }

    [Fact]
    public void AccessTokens_LastOneHour()
    {
        var token = ReadToken(CreateService().CreateAccessToken(SampleUser()));
        var lifetime = token.ValidTo - token.ValidFrom;

        Assert.InRange(lifetime.TotalMinutes, 59, 61);
    }

    [Fact]
    public void ResidentAccessTokens_LastOneHour()
    {
        var token = ReadToken(CreateService().CreateResidentAccessToken(SampleResident(), LicenseId));
        var lifetime = token.ValidTo - token.ValidFrom;

        Assert.InRange(lifetime.TotalMinutes, 59, 61);
    }

    private static JwtSecurityToken ReadToken(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    private static JwtTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:Secret"] = "unit-test-signing-secret-unit-test-signing-secret",
                ["JWT:Issuer"] = "Condotify.Tests",
                ["JWT:Audience"] = "Condotify.Tests"
            })
            .Build();
        return new JwtTokenService(configuration);
    }

    private static UserAccess SampleUser(bool firstAccess = false) => UserAccess.Create(
        "Lucas Bastos",
        "lucas@test.com",
        "Senha123!",
        "11999999999",
        "12345678900",
        "1234567",
        "1990-01-01",
        AccessTypeEnum.Admin,
        firstAccess,
        DateTime.UtcNow,
        DateTime.UtcNow,
        Hasher,
        Guid.NewGuid());

    private static ResidentAccessDTO SampleResident() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Maria Moradora",
        Email = "maria@test.com",
        UnitId = Guid.NewGuid()
    };
}

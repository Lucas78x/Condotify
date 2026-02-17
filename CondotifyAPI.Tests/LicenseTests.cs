using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.License;

namespace CondotifyAPI.Tests
{
    public class LicenseTests
    {
        //[Fact]
        //public void Create_ShouldReturnLicenseWithCorrectProperties()
        //{
        //    // Arrange
        //    var name = "Produto Teste";
        //    var type = LicenseTypeEnum.Demo;
        //    var location = Location.Create("SP",0f, 0f);
        //    var expireDate = DateTime.UtcNow.AddYears(1);
        //    var createdAt = DateTime.UtcNow;

        //    // Act
        //    var license = License.Create(name, type, location, expireDate, createdAt);

        //    // Assert
        //    Assert.NotNull(license);
        //    Assert.Equal(name, license.Name);
        //    Assert.Equal(type, license.Type);
        //    Assert.Equal(location, license.Location);
        //    Assert.Equal(expireDate, license.ExpireDate);
        //    Assert.Equal(createdAt, license.CreatedAt);
        //    Assert.NotEqual(Guid.Empty, license.Id);
        //}

        //[Fact]
        //public void Create_ShouldGenerateUniqueIdForEachLicense()
        //{
        //    // Act
        //    var license1 = License.Create("Produto1", LicenseTypeEnum.Demo, new Location(), DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);
        //    var license2 = License.Create("Produto2", LicenseTypeEnum.Full, new Location(), DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);

        //    // Assert
        //    Assert.NotEqual(license1.Id, license2.Id);
        //}

        //[Fact]
        //public void Create_WithNullLocation_ShouldSetLocationProperty()
        //{
        //    // Act
        //    var license = License.Create("Produto Teste", LicenseTypeEnum.Demo, null, DateTime.UtcNow.AddYears(1), DateTime.UtcNow);

        //    // Assert
        //    Assert.Null(license.Location);
        //}

        //[Fact]
        //public void IsExpired_ShouldReturnTrue_WhenLicenseIsExpired()
        //{
        //    // Arrange
        //    var expiredLicense = License.Create(
        //        name: "Produto Expirado",
        //        type: LicenseTypeEnum.Demo,
        //        location: new Location(),
        //        expireDate: DateTime.UtcNow.AddDays(-1), 
        //        createdAt: DateTime.UtcNow.AddMonths(-1)
        //    );

        //    // Act
        //    var result = expiredLicense.IsExpired();

        //    // Assert
        //    Assert.True(result);
        //}

        //[Fact]
        //public void IsExpired_ShouldReturnFalse_WhenLicenseIsNotExpired()
        //{
        //    // Arrange
        //    var validLicense = License.Create(
        //        name: "Produto Valido",
        //        type: LicenseTypeEnum.Demo,
        //        location: new Location(),
        //        expireDate: DateTime.UtcNow.AddDays(1),
        //        createdAt: DateTime.UtcNow
        //    );

        //    // Act
        //    var result = validLicense.IsExpired();

        //    // Assert
        //    Assert.False(result);
        //}
    }
}

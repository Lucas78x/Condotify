using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.License;

namespace CondotifyAPI.Tests
{
    public class LicenseTests
    {
        [Fact]
        public void Create_ShouldReturnLicenseWithExpectedProperties()
        {
            var location = Location.Create("SP", 1.5f, 2.5f);
            var expireDate = DateTime.UtcNow.AddYears(1);
            var createdAt = DateTime.UtcNow;

            var license = License.Create(
                "Condominio Central",
                "12345678000190",
                LicenseTypeEnum.Full,
                location,
                expireDate,
                createdAt);

            Assert.NotEqual(Guid.Empty, license.Id);
            Assert.Equal("Condominio Central", license.Name);
            Assert.Equal("12345678000190", license.CNPJ);
            Assert.Equal(LicenseTypeEnum.Full, license.Type);
            Assert.Equal(location, license.Location);
            Assert.Equal(expireDate, license.ExpireDate);
            Assert.Equal(createdAt, license.CreatedAt);
        }

        [Fact]
        public void IsExpired_ShouldReturnExpectedState()
        {
            var expired = License.Create(
                "Expirada",
                "12345678000190",
                LicenseTypeEnum.Demo,
                new Location(),
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddMonths(-1));

            var active = License.Create(
                "Ativa",
                "12345678000191",
                LicenseTypeEnum.Full,
                new Location(),
                DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow);

            Assert.True(expired.IsExpired());
            Assert.False(active.IsExpired());
        }

        [Fact]
        public void AddBlock_ShouldInitializeCollectionAndAddBlock()
        {
            var license = License.Create(
                "Com blocos",
                "12345678000190",
                LicenseTypeEnum.Full,
                new Location(),
                DateTime.UtcNow.AddYears(1),
                DateTime.UtcNow);

            var result = license.AddBlock(new Block { Id = Guid.NewGuid(), Name = "Bloco A" });

            Assert.True(result);
            Assert.Single(license.Blocks);
            Assert.Equal("Bloco A", license.Blocks[0].Name);
        }

        [Fact]
        public void MaskCNPJ_ShouldMaskValidCnpj()
        {
            var license = License.Create(
                "Com CNPJ",
                "12345678000190",
                LicenseTypeEnum.Full,
                new Location(),
                DateTime.UtcNow.AddYears(1),
                DateTime.UtcNow);

            license.MaskCNPJ();

            Assert.Equal("12.***.***/0001-**", license.CNPJ);
        }
    }
}

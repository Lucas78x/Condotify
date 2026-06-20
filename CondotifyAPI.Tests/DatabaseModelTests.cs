using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests
{
    public class DatabaseModelTests
    {
        [Fact]
        public void UserAccess_ShouldHaveUniqueIndexesForNaturalKeys()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(UserAccessDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(UserAccessDTO.Email)));
            Assert.True(HasUniqueIndex(entity!, nameof(UserAccessDTO.CPF)));
            Assert.True(HasUniqueIndex(entity!, nameof(UserAccessDTO.RG)));
            Assert.True(HasUniqueIndex(entity!, nameof(UserAccessDTO.PhoneNumber)));
        }

        [Fact]
        public void Enterprise_ShouldHaveUniqueIndexesForCnpjAndEmail()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(EnterpriseDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(EnterpriseDTO.CNPJ)));
            Assert.True(HasUniqueIndex(entity!, nameof(EnterpriseDTO.Email)));
        }

        [Fact]
        public void License_ShouldHaveUniqueIndexPerEnterpriseAndName()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(LicenseDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(LicenseDTO.EnterpriseId), nameof(LicenseDTO.Name)));
        }

        [Fact]
        public void Devices_ShouldHaveUniqueIndexesScopedByLicense()
        {
            using var context = CreateContext();
            var accessDevice = context.Model.FindEntityType(typeof(AccessControlDeviceDTO));
            var cftvDevice = context.Model.FindEntityType(typeof(CFTVDeviceDTO));

            Assert.NotNull(accessDevice);
            Assert.NotNull(cftvDevice);
            Assert.True(HasUniqueIndex(accessDevice!, nameof(AccessControlDeviceDTO.LicenseId), nameof(AccessControlDeviceDTO.SerialNumber)));
            Assert.True(HasUniqueIndex(accessDevice!, nameof(AccessControlDeviceDTO.LicenseId), nameof(AccessControlDeviceDTO.MACAddress)));
            Assert.True(HasUniqueIndex(cftvDevice!, nameof(CFTVDeviceDTO.LicenseId), nameof(CFTVDeviceDTO.IpAddress), nameof(CFTVDeviceDTO.HTTPPort), nameof(CFTVDeviceDTO.RTSPPort)));
        }

        [Fact]
        public void CondominiumStructure_ShouldHaveScopedUniqueIndexes()
        {
            using var context = CreateContext();
            var block = context.Model.FindEntityType(typeof(BlockDTO));
            var unit = context.Model.FindEntityType(typeof(UnitDTO));
            var resident = context.Model.FindEntityType(typeof(ResidentAccessDTO));

            Assert.NotNull(block);
            Assert.NotNull(unit);
            Assert.NotNull(resident);
            Assert.True(HasUniqueIndex(block!, nameof(BlockDTO.LicenseId), nameof(BlockDTO.Name)));
            Assert.True(HasUniqueIndex(unit!, nameof(UnitDTO.BlockId), nameof(UnitDTO.Number)));
            Assert.True(HasUniqueIndex(resident!, nameof(ResidentAccessDTO.UnitId), nameof(ResidentAccessDTO.CPF)));
        }

        private static DatabaseContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<DatabaseContext>()
                .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
                .Options;

            return new DatabaseContext(options);
        }

        private static bool HasUniqueIndex(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, params string[] propertyNames)
        {
            return entity.GetIndexes().Any(index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        }
    }
}

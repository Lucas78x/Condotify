using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests
{
    public class DatabaseModelTests
    {
        public DatabaseModelTests()
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET", "condotify-tests-equipment-secret-2026");
        }

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

        [Fact]
        public void PropertyPeopleAndVehicles_ShouldKeepTenantScopedIntegrity()
        {
            using var context = CreateContext();
            var link = context.Model.FindEntityType(typeof(ResidentUnitLinkDTO));
            var vehicle = context.Model.FindEntityType(typeof(VehicleDTO));
            var invite = context.Model.FindEntityType(typeof(RegistrationInviteDTO));

            Assert.NotNull(link);
            Assert.NotNull(vehicle);
            Assert.NotNull(invite);
            Assert.True(HasUniqueIndex(link!, nameof(ResidentUnitLinkDTO.ResidentId), nameof(ResidentUnitLinkDTO.UnitId)));
            Assert.True(HasUniqueIndex(vehicle!, nameof(VehicleDTO.UnitId), nameof(VehicleDTO.Plate)));
            Assert.True(HasUniqueIndex(invite!, nameof(RegistrationInviteDTO.TokenHash)));
            Assert.True(HasIndex(invite!, nameof(RegistrationInviteDTO.LicenseId), nameof(RegistrationInviteDTO.Status), nameof(RegistrationInviteDTO.SentAt)));
        }

        [Fact]
        public void Deliveries_ShouldHaveLookupIndexesScopedByLicense()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(DeliveryDTO));

            Assert.NotNull(entity);
            Assert.True(HasIndex(entity!, nameof(DeliveryDTO.LicenseId), nameof(DeliveryDTO.Status), nameof(DeliveryDTO.CreatedAt)));
            Assert.True(HasIndex(entity!, nameof(DeliveryDTO.LicenseId), nameof(DeliveryDTO.TrackingCode)));
        }

        [Fact]
        public void LicenseAdministration_ShouldKeepUserScopeAndSinglePolicy()
        {
            using var context = CreateContext();
            var access = context.Model.FindEntityType(typeof(LicenseUserAccessDTO));
            var policy = context.Model.FindEntityType(typeof(LicenseCredentialPolicyDTO));

            Assert.NotNull(access);
            Assert.NotNull(policy);
            Assert.True(HasUniqueIndex(access!, nameof(LicenseUserAccessDTO.LicenseId), nameof(LicenseUserAccessDTO.UserId)));
            Assert.True(HasIndex(access!, nameof(LicenseUserAccessDTO.UserId), nameof(LicenseUserAccessDTO.IsActive)));
            Assert.Equal(nameof(LicenseCredentialPolicyDTO.LicenseId), policy!.FindPrimaryKey()!.Properties.Single().Name);
        }

        [Fact]
        public void CredentialDeletion_ShouldCascadeToDeviceBindings()
        {
            using var context = CreateContext();
            var binding = context.Model.FindEntityType(typeof(CondotifyAPI.Domain.Enums.Resident.ResidentAccessDeviceDTO));

            Assert.NotNull(binding);
            var foreignKey = binding!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(ResidentAccessCredentialDTO));
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        }

        [Fact]
        public void AccessRoutes_ShouldBeScopedAndKeepUniqueDevicePortals()
        {
            using var context = CreateContext();
            var route = context.Model.FindEntityType(typeof(AccessRouteDTO));
            var target = context.Model.FindEntityType(typeof(AccessRouteDeviceDTO));

            Assert.NotNull(route);
            Assert.NotNull(target);
            Assert.True(HasUniqueIndex(route!, nameof(AccessRouteDTO.LicenseId), nameof(AccessRouteDTO.Name)));
            Assert.True(HasUniqueIndex(target!, nameof(AccessRouteDeviceDTO.AccessRouteId), nameof(AccessRouteDeviceDTO.DeviceId), nameof(AccessRouteDeviceDTO.PortalNumber)));
            Assert.All(target!.GetForeignKeys(), foreignKey => Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior));
        }

        [Fact]
        public void Amenities_ShouldBeScopedByLicenseAndUniquelyNamed()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(AmenityDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(AmenityDTO.LicenseId), nameof(AmenityDTO.Name)));
        }

        [Fact]
        public void AmenityBookings_ShouldPreventDoubleBookingTheSameSlot()
        {
            using var context = CreateContext();
            var booking = context.Model.FindEntityType(typeof(AmenityBookingDTO));

            Assert.NotNull(booking);
            var index = booking!.GetIndexes().Single(x =>
                x.IsUnique &&
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AmenityBookingDTO.AmenityId), nameof(AmenityBookingDTO.SlotId), nameof(AmenityBookingDTO.Date) }));

            Assert.Equal("\"Status\" IN (0, 1)", index.GetFilter());
        }

        [Fact]
        public void AmenityBookings_ShouldRestrictSlotDeletionButCascadeAmenityDeletion()
        {
            using var context = CreateContext();
            var booking = context.Model.FindEntityType(typeof(AmenityBookingDTO));

            Assert.NotNull(booking);
            var slotForeignKey = booking!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(AmenityScheduleSlotDTO));
            var amenityForeignKey = booking.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(AmenityDTO));

            Assert.Equal(DeleteBehavior.Restrict, slotForeignKey.DeleteBehavior);
            Assert.Equal(DeleteBehavior.Cascade, amenityForeignKey.DeleteBehavior);
        }

        [Fact]
        public void AccessOperations_ShouldDeduplicateEventsAndRouteOverrides()
        {
            using var context = CreateContext();
            var accessEvent = context.Model.FindEntityType(typeof(AccessEventRecordDTO));
            var routeOverride = context.Model.FindEntityType(typeof(AccessRouteResidentOverrideDTO));

            Assert.NotNull(accessEvent);
            Assert.NotNull(routeOverride);
            Assert.True(HasUniqueIndex(accessEvent!, nameof(AccessEventRecordDTO.DeviceId), nameof(AccessEventRecordDTO.ExternalEventId)));
            Assert.True(HasUniqueIndex(routeOverride!, nameof(AccessRouteResidentOverrideDTO.AccessRouteId), nameof(AccessRouteResidentOverrideDTO.ResidentId)));
        }

        [Fact]
        public void AdvancedOperations_ShouldBeIdempotentAndTrackEachDeviceTarget()
        {
            using var context = CreateContext();
            var batch = context.Model.FindEntityType(typeof(AccessBatchOperationDTO));
            var item = context.Model.FindEntityType(typeof(AccessOperationItemDTO));

            Assert.NotNull(batch);
            Assert.NotNull(item);
            Assert.True(HasUniqueIndex(batch!, nameof(AccessBatchOperationDTO.LicenseId), nameof(AccessBatchOperationDTO.IdempotencyKey)));
            Assert.True(HasUniqueIndex(item!, nameof(AccessOperationItemDTO.IdempotencyKey)));
            Assert.True(HasIndex(item!, nameof(AccessOperationItemDTO.Status), nameof(AccessOperationItemDTO.NextAttemptAt)));
        }

        [Fact]
        public void DeviceInventory_ShouldDeduplicateRemoteRecordsPerDevice()
        {
            using var context = CreateContext();
            var inventory = context.Model.FindEntityType(typeof(AccessInventoryItemDTO));

            Assert.NotNull(inventory);
            Assert.True(HasUniqueIndex(inventory!, nameof(AccessInventoryItemDTO.DeviceId), nameof(AccessInventoryItemDTO.RemoteKey)));
            Assert.True(HasIndex(inventory!, nameof(AccessInventoryItemDTO.LicenseId), nameof(AccessInventoryItemDTO.Status)));
        }

        [Fact]
        public void Visits_ShouldKeepCredentialUniqueAndTenantLookupIndexed()
        {
            using var context = CreateContext();
            var visit = context.Model.FindEntityType(typeof(AccessVisitDTO));

            Assert.NotNull(visit);
            Assert.True(HasUniqueIndex(visit!, nameof(AccessVisitDTO.CredentialId)));
            Assert.True(HasIndex(visit!, nameof(AccessVisitDTO.LicenseId), nameof(AccessVisitDTO.Status), nameof(AccessVisitDTO.ValidFrom)));
            Assert.Equal(DeleteBehavior.Restrict, visit!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(ResidentAccessCredentialDTO)).DeleteBehavior);
        }

        [Fact]
        public void EquipmentPasswords_ShouldUseEncryptedProviderConversion()
        {
            using var context = CreateContext();
            var device = context.Model.FindEntityType(typeof(AccessControlDeviceDTO));
            var password = device!.FindProperty(nameof(AccessControlDeviceDTO.Password));
            var converter = password!.GetValueConverter();

            Assert.NotNull(converter);
            var encrypted = Assert.IsType<string>(converter!.ConvertToProvider("admin-secret"));
            Assert.StartsWith("enc:v1:", encrypted);
            Assert.DoesNotContain("admin-secret", encrypted);
            Assert.Equal("admin-secret", converter.ConvertFromProvider(encrypted));
        }

        [Fact]
        public void DateTimes_ShouldBeStoredAsUtcTimestamps()
        {
            using var context = CreateContext();

            var dateProperties = context.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties())
                .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                .ToList();

            Assert.NotEmpty(dateProperties);
            Assert.All(dateProperties, property => Assert.Equal("timestamp with time zone", property.GetColumnType()));
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

        private static bool HasIndex(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, params string[] propertyNames)
        {
            return entity.GetIndexes().Any(index =>
                index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        }
    }
}

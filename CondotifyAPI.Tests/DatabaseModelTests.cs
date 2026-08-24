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
using CondotifyAPI.Domain.DTO.RecycleBin;
using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Mobile;
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
            var residentCpfIndex = resident!.GetIndexes().Single(x =>
                x.IsUnique &&
                x.Properties.Select(p => p.Name).SequenceEqual(
                    [nameof(ResidentAccessDTO.UnitId), nameof(ResidentAccessDTO.CPF)]));
            Assert.Equal("\"CPF\" <> ''", residentCpfIndex.GetFilter());
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
        public void ResidentPasswordRecoveryTokens_ShouldHaveUniqueTokenHashAndResidentLookupIndex()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(ResidentPasswordRecoveryTokenDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(ResidentPasswordRecoveryTokenDTO.TokenHash)));
            Assert.True(HasIndex(entity!, nameof(ResidentPasswordRecoveryTokenDTO.ResidentId), nameof(ResidentPasswordRecoveryTokenDTO.UsedAt)));
        }

        [Fact]
        public void RecycleBin_ShouldEncryptSnapshotsAndKeepRetentionIndexes()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(RecycleBinItemDTO));

            Assert.NotNull(entity);
            Assert.True(HasIndex(
                entity!,
                nameof(RecycleBinItemDTO.LicenseId),
                nameof(RecycleBinItemDTO.RestoredAt),
                nameof(RecycleBinItemDTO.ExpiresAt)));
            Assert.True(HasIndex(
                entity!,
                nameof(RecycleBinItemDTO.LicenseId),
                nameof(RecycleBinItemDTO.EntityType),
                nameof(RecycleBinItemDTO.EntityId)));

            var property = entity!.FindProperty(nameof(RecycleBinItemDTO.SnapshotJson));
            var converter = property!.GetValueConverter();
            Assert.NotNull(converter);
            const string snapshot = """{"password":"secret-value","name":"Portaria"}""";
            var protectedValue = Assert.IsType<string>(converter!.ConvertToProvider(snapshot));

            Assert.StartsWith("enc:v1:", protectedValue, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-value", protectedValue, StringComparison.Ordinal);
            Assert.Equal(snapshot, converter.ConvertFromProvider(protectedValue));
        }

        [Fact]
        public void ConfigurationBackups_ShouldEncryptPayloadAndVersionPerLicense()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(ConfigurationBackupDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(
                entity!,
                nameof(ConfigurationBackupDTO.LicenseId),
                nameof(ConfigurationBackupDTO.Version)));
            Assert.True(HasIndex(
                entity!,
                nameof(ConfigurationBackupDTO.LicenseId),
                nameof(ConfigurationBackupDTO.CreatedAt)));

            var property = entity!.FindProperty(nameof(ConfigurationBackupDTO.PayloadJson));
            var converter = property!.GetValueConverter();
            Assert.NotNull(converter);
            const string payload = """{"devices":[{"password":"terminal-secret"}]}""";
            var protectedValue = Assert.IsType<string>(converter!.ConvertToProvider(payload));

            Assert.StartsWith("enc:v1:", protectedValue, StringComparison.Ordinal);
            Assert.DoesNotContain("terminal-secret", protectedValue, StringComparison.Ordinal);
            Assert.Equal(payload, converter.ConvertFromProvider(protectedValue));
        }

        [Fact]
        public void BackupAutomation_ShouldKeepSinglePolicyAndDueLookupIndex()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(BackupAutomationPolicyDTO));

            Assert.NotNull(entity);
            Assert.Equal(
                nameof(BackupAutomationPolicyDTO.LicenseId),
                entity!.FindPrimaryKey()!.Properties.Single().Name);
            Assert.True(HasIndex(
                entity,
                nameof(BackupAutomationPolicyDTO.Enabled),
                nameof(BackupAutomationPolicyDTO.NextRunAt),
                nameof(BackupAutomationPolicyDTO.LeaseExpiresAt)));
        }

        [Fact]
        public void Deliveries_ShouldHaveLookupIndexesScopedByLicense()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(DeliveryDTO));

            Assert.NotNull(entity);
            Assert.True(HasIndex(entity!, nameof(DeliveryDTO.LicenseId), nameof(DeliveryDTO.Status), nameof(DeliveryDTO.CreatedAt)));
            Assert.True(HasIndex(entity!, nameof(DeliveryDTO.LicenseId), nameof(DeliveryDTO.TrackingCode)));
            Assert.True(HasIndex(entity!, nameof(DeliveryDTO.RecipientResidentId), nameof(DeliveryDTO.Status), nameof(DeliveryDTO.CreatedAt)));

            var residentForeignKey = entity.GetForeignKeys()
                .Single(x => x.PrincipalEntityType.ClrType == typeof(ResidentAccessDTO));
            var unitForeignKey = entity.GetForeignKeys()
                .Single(x => x.PrincipalEntityType.ClrType == typeof(UnitDTO));
            Assert.Equal(DeleteBehavior.SetNull, residentForeignKey.DeleteBehavior);
            Assert.Equal(DeleteBehavior.SetNull, unitForeignKey.DeleteBehavior);
        }

        [Fact]
        public void CftvResidentVisibility_ShouldDefaultToPrivate()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(CFTVDeviceDTO));

            Assert.NotNull(entity);
            Assert.Equal(false, entity!.FindProperty(nameof(CFTVDeviceDTO.ResidentVisible))!.GetDefaultValue());
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

        [Fact]
        public void OperationalAlerts_ShouldBeDeduplicatedAndIndexedForTheWorkQueue()
        {
            using var context = CreateContext();
            var alert = context.Model.FindEntityType(typeof(OperationalAlertDTO));

            Assert.NotNull(alert);
            Assert.True(HasUniqueIndex(
                alert!,
                nameof(OperationalAlertDTO.EnterpriseId),
                nameof(OperationalAlertDTO.Fingerprint)));
            Assert.True(HasIndex(
                alert!,
                nameof(OperationalAlertDTO.EnterpriseId),
                nameof(OperationalAlertDTO.Status),
                nameof(OperationalAlertDTO.Severity),
                nameof(OperationalAlertDTO.LastOccurredAt)));

            var licenseForeignKey = alert!.GetForeignKeys()
                .Single(x => x.PrincipalEntityType.ClrType == typeof(LicenseDTO));
            Assert.Equal(DeleteBehavior.SetNull, licenseForeignKey.DeleteBehavior);
        }

        [Fact]
        public void AlertNotifications_ShouldEncryptDestinationsAndDeduplicateDeliveries()
        {
            using var context = CreateContext();
            var policy = context.Model.FindEntityType(typeof(AlertNotificationPolicyDTO));
            var delivery = context.Model.FindEntityType(typeof(AlertNotificationDeliveryDTO));

            Assert.NotNull(policy);
            Assert.NotNull(delivery);
            Assert.Equal(
                nameof(AlertNotificationPolicyDTO.LicenseId),
                policy!.FindPrimaryKey()!.Properties.Single().Name);
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.WebhookUrl))!.GetValueConverter());
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.WebhookSecret))!.GetValueConverter());
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.EmailRecipients))!.GetValueConverter());
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.SmtpUsername))!.GetValueConverter());
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.SmtpPassword))!.GetValueConverter());
            Assert.NotNull(policy.FindProperty(nameof(AlertNotificationPolicyDTO.SmtpFromEmail))!.GetValueConverter());
            Assert.True(HasUniqueIndex(delivery!, nameof(AlertNotificationDeliveryDTO.DeliveryKey)));
            Assert.True(HasIndex(
                delivery!,
                nameof(AlertNotificationDeliveryDTO.Status),
                nameof(AlertNotificationDeliveryDTO.NextAttemptAt),
                nameof(AlertNotificationDeliveryDTO.LeaseExpiresAt)));
        }

        [Fact]
        public void MobilePush_ShouldEncryptTokensAndDeduplicateInstallationsAndDeliveries()
        {
            using var context = CreateContext();
            var installation = context.Model.FindEntityType(typeof(PushInstallationDTO));
            var notification = context.Model.FindEntityType(typeof(PushNotificationDTO));
            var delivery = context.Model.FindEntityType(typeof(PushDeliveryDTO));

            Assert.NotNull(installation);
            Assert.NotNull(notification);
            Assert.NotNull(delivery);
            Assert.True(HasUniqueIndex(installation!, nameof(PushInstallationDTO.InstallationId)));
            Assert.True(HasUniqueIndex(installation!, nameof(PushInstallationDTO.TokenHash)));
            Assert.True(HasUniqueIndex(
                notification!,
                nameof(PushNotificationDTO.SubjectType),
                nameof(PushNotificationDTO.SubjectId),
                nameof(PushNotificationDTO.DeduplicationKey)));
            Assert.True(HasUniqueIndex(
                delivery!,
                nameof(PushDeliveryDTO.NotificationId),
                nameof(PushDeliveryDTO.InstallationId)));

            var converter = installation.FindProperty(nameof(PushInstallationDTO.PushToken))!.GetValueConverter();
            Assert.NotNull(converter);
            var encrypted = Assert.IsType<string>(converter!.ConvertToProvider("fcm-sensitive-token"));
            Assert.StartsWith("enc:v1:", encrypted);
            Assert.DoesNotContain("fcm-sensitive-token", encrypted);
            Assert.Equal("fcm-sensitive-token", converter.ConvertFromProvider(encrypted));
        }

        [Fact]
        public void ResidentAccessCredential_ShouldSupportArchivingWithoutDeletingVisitHistory()
        {
            using var context = CreateContext();
            var credential = context.Model.FindEntityType(typeof(ResidentAccessCredentialDTO));
            var visit = context.Model.FindEntityType(typeof(AccessVisitDTO));

            Assert.NotNull(credential);
            Assert.NotNull(visit);
            Assert.True(HasIndex(credential!, nameof(ResidentAccessCredentialDTO.ArchivedAt)));

            var credentialForeignKey = visit!.GetForeignKeys()
                .Single(x => x.PrincipalEntityType.ClrType == typeof(ResidentAccessCredentialDTO));
            Assert.Equal(DeleteBehavior.Restrict, credentialForeignKey.DeleteBehavior);
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

        [Fact]
        public void LicenseModuleEnum_AllCoversExactlyTheTwelveOptionalModules()
        {
            var expected = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Devices
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Routes
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Automations
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Emergency
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Deliveries
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Finance
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Documents
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Announcements
                | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Assemblies;

            Assert.Equal(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All, expected);
            Assert.Equal(4095L, (long)CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All);
        }

        [Fact]
        public void License_EnabledModulesDefaultsToAllOptionalModules()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(CondotifyAPI.Domain.DTO.License.LicenseDTO));

            Assert.NotNull(entity);
            var property = entity!.FindProperty(nameof(CondotifyAPI.Domain.DTO.License.LicenseDTO.EnabledModules));
            Assert.NotNull(property);
            Assert.Equal(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All, property!.GetDefaultValue());
        }
    }
}

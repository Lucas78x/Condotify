using System;
using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.Extensions;
using Xunit;

namespace CondotifyAPI.Tests
{
    public class AccessControlDeviceTests
    {
        [Fact]
        public void Create_ShouldInitializePropertiesCorrectly()
        {
            // Arrange
            var name = "Facial Device 1";
            var ip = "192.168.1.10";
            var port = 8000;
            var username = "admin";
            var password = "12345";
            var mac = "AA:BB:CC:DD:EE:FF";
            var model = "DS-K1T671MF";
            var serial = "SN123456";
            var firmware = "V1.2.3";
            var type = DeviceTypeEnum.SS5541MFW;
            var isActive = true;
            var location = Location.Create("SP", 10.5f, 20.3f);
            var createdAt = DateTime.UtcNow;
            var lastUpdatedAt = DateTime.UtcNow;

            // Act
            var device = AccessControlDevice.Create(
                name, ip, port, username, password, mac, model, serial, firmware, type, isActive, location, createdAt, lastUpdatedAt
            );

            // Assert
            Assert.NotNull(device);
            Assert.Equal(name, device.Name);
            Assert.Equal(ip, device.IPAddress);
            Assert.Equal(port, device.Port);
            Assert.Equal(username, device.Username);
            Assert.Equal(password, device.Password);
            Assert.Equal(mac, device.MACAddress);
            Assert.Equal(model, device.Model);
            Assert.Equal(serial, device.SerialNumber);
            Assert.Equal(firmware, device.FirmwareVersion);
            Assert.Equal(type, device.Type);
            Assert.Equal(isActive, device.IsActive);
            Assert.Equal(location, device.Location);
            Assert.Equal(createdAt, device.CreatedAt);
            Assert.Equal(lastUpdatedAt, device.LastUpdatedAt);
            Assert.NotEqual(Guid.Empty, device.Id);
        }

        [Fact]
        public void Update_ShouldModifyPropertiesCorrectly()
        {
            // Arrange
            var device = AccessControlDevice.Create(
                "Old Device", "192.168.1.20", 8080, "oldUser", "oldPass", "11:22:33:44:55:66",
                "OldModel", "OLD123", "V1.0.0", DeviceTypeEnum.SS3531MF, true, Location.Create("SP", 1f, 2f),
                DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-10)
            );

            var newName = "New Device";
            var newIp = "192.168.1.30";
            var newPort = 9000;
            var newUsername = "newUser";
            var newPassword = "newPass";
            var newMac = "FF:EE:DD:CC:BB:AA";
            var newModel = "NewModel";
            var newSerial = "NEW123";
            var newFirmware = "V2.0.0";
            var newType = DeviceTypeEnum.SS5541MFW;
            var newIsActive = false;
            var newLocation = Location.Create("SP", 50.5f, 60.6f);
            var newLastUpdated = DateTime.UtcNow;

            // Act
            var updated = device.Update(
                newName, newIp, newPort, newUsername, newPassword, newMac, newModel, newSerial,
                newFirmware, newType, newIsActive, newLocation, newLastUpdated
            );

            // Assert
            Assert.True(updated);
            Assert.Equal(newName, device.Name);
            Assert.Equal(newIp, device.IPAddress);
            Assert.Equal(newPort, device.Port);
            Assert.Equal(newUsername, device.Username);
            Assert.Equal(newPassword, device.Password);
            Assert.Equal(newMac, device.MACAddress);
            Assert.Equal(newModel, device.Model);
            Assert.Equal(newSerial, device.SerialNumber);
            Assert.Equal(newFirmware, device.FirmwareVersion);
            Assert.Equal(newType, device.Type);
            Assert.Equal(newIsActive, device.IsActive);
            Assert.Equal(newLocation, device.Location);
            Assert.Equal(newLastUpdated, device.LastUpdatedAt);
        }

        [Fact]
        public void Id_ShouldBeUnique_OnEachCreation()
        {
            // Arrange & Act
            var device1 = AccessControlDevice.Create(
                "Device1", "192.168.1.40", 7000, "u1", "p1", "00:11:22:33:44:55",
                "Model1", "S1", "FW1",DeviceTypeEnum.SS5520, true, Location.Create("SP", 1f, 1f), DateTime.UtcNow, DateTime.UtcNow
            );

            var device2 = AccessControlDevice.Create(
                "Device2", "192.168.1.41", 7001, "u2", "p2", "66:77:88:99:AA:BB",
                "Model2", "S2", "FW2",DeviceTypeEnum.SS5530MFFace, false, Location.Create("SP", 2f, 2f), DateTime.UtcNow, DateTime.UtcNow
            );

            // Assert
            Assert.NotEqual(device1.Id, device2.Id);
        }

        [Fact]
        public void InputConverter_ShouldUseDeviceNameForUnnamedLocation_AndKeepOptionalFieldsNull()
        {
            var input = new CreateAccessControlDeviceByLicenseIn
            {
                LicenseId = Guid.NewGuid().ToString(),
                Name = "Portaria principal",
                IPAddress = "192.168.1.50",
                Port = 80,
                Username = "admin",
                Password = "secret",
                Model = "SS5520",
                Type = DeviceTypeEnum.SS5520,
                Location = new Location { X = -12.9f, Y = -38.5f }
            };

            var command = input.ToCommand();

            Assert.Equal("Portaria principal", command.Name);
            Assert.Equal("Portaria principal", command.Location.Name);
            Assert.Null(command.MACAddress);
            Assert.Null(command.SerialNumber);
            Assert.Null(command.FirmwareVersion);
        }

        [Theory]
        [InlineData(DeviceTypeEnum.SS5520, true)]
        [InlineData(DeviceTypeEnum.IdFaceMax, true)]
        [InlineData(DeviceTypeEnum.HikvisionDSK1T673, true)]
        [InlineData(DeviceTypeEnum.HikvisionDSK1T671, true)]
        [InlineData(DeviceTypeEnum.HikvisionDSK1T323, true)]
        [InlineData(DeviceTypeEnum.SS3710UHF, false)]
        [InlineData(DeviceTypeEnum.ControlIdUHF, false)]
        [InlineData(DeviceTypeEnum.CT30002PB, false)]
        public void SupportsFace_ShouldMatchDeviceCapability(DeviceTypeEnum type, bool expected)
        {
            Assert.Equal(expected, type.SupportsFace());
        }

        [Fact]
        public void HikvisionPayloadReader_ShouldUseXmlFallbackWithoutTryingJsonParser()
        {
            const string xml = "<DeviceInfo><model>DS-K1T671</model><serialNumber>ABC123</serialNumber></DeviceInfo>";

            var jsonModel = HikvisionAccessControlDriver.ReadJsonString(xml, "DeviceInfo", "model");
            var xmlModel = HikvisionAccessControlDriver.ReadXmlValue(xml, "model");

            Assert.Empty(jsonModel);
            Assert.Equal("DS-K1T671", xmlModel);
        }
    }
}

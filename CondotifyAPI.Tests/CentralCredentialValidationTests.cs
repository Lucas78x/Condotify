using CondotifyAPI.Controllers;
using CondotifyAPI.Data.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Tests;

public sealed class CentralCredentialValidationTests
{
    [Theory]
    [InlineData(AccessCredentialTypeEnum.QrCode)]
    [InlineData(AccessCredentialTypeEnum.Card)]
    [InlineData(AccessCredentialTypeEnum.Tag)]
    [InlineData(AccessCredentialTypeEnum.VehicleTag)]
    [InlineData(AccessCredentialTypeEnum.Password)]
    public void CentralRegistration_RequiresNoDeviceForSupportedTypes(AccessCredentialTypeEnum type)
    {
        var input = new CreateCredentialIn { ResidentId = Guid.NewGuid(), SaveWithoutDevice = true, Type = type, Identifier = "123456" };
        Assert.Null(CredentialManagementController.ValidateInput(input));
    }

    [Fact]
    public void FacialRegistration_CannotDiscardImageByDeferringDistribution()
    {
        var input = new CreateCredentialIn { ResidentId = Guid.NewGuid(), SaveWithoutDevice = true, Type = AccessCredentialTypeEnum.Face };
        Assert.NotNull(CredentialManagementController.ValidateInput(input));
    }

    [Fact]
    public void MissingDevice_RequiresExplicitChoiceToDefer()
    {
        var input = new CreateCredentialIn { ResidentId = Guid.NewGuid(), Type = AccessCredentialTypeEnum.QrCode };
        Assert.NotNull(CredentialManagementController.ValidateInput(input));
        input.SaveWithoutDevice = true;
        Assert.Null(CredentialManagementController.ValidateInput(input));
        input.DeviceId = Guid.NewGuid();
        Assert.NotNull(CredentialManagementController.ValidateInput(input));
    }
}

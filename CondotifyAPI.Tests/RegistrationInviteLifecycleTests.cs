using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.Enums.Invitation;

namespace CondotifyAPI.Tests;

public sealed class RegistrationInviteLifecycleTests
{
    [Theory]
    [InlineData(RegistrationInviteStatusEnum.Pending, true)]
    [InlineData(RegistrationInviteStatusEnum.Opened, true)]
    [InlineData(RegistrationInviteStatusEnum.Completed, false)]
    [InlineData(RegistrationInviteStatusEnum.Expired, false)]
    [InlineData(RegistrationInviteStatusEnum.Canceled, false)]
    public void Cancel_AcceptsOnlyUsableInvites(RegistrationInviteStatusEnum status, bool expected) =>
        Assert.Equal(expected, PeopleManagementController.CanCancelInvite(status));

    [Theory]
    [InlineData(RegistrationInviteStatusEnum.Pending, true)]
    [InlineData(RegistrationInviteStatusEnum.Opened, true)]
    [InlineData(RegistrationInviteStatusEnum.Expired, true)]
    [InlineData(RegistrationInviteStatusEnum.Canceled, true)]
    [InlineData(RegistrationInviteStatusEnum.Completed, false)]
    public void Reissue_RejectsOnlyCompletedInvite(RegistrationInviteStatusEnum status, bool expected) =>
        Assert.Equal(expected, PeopleManagementController.CanReissueInvite(status));
}

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

    [Theory]
    [InlineData(RegistrationInviteStatusEnum.Pending, -1, true)]
    [InlineData(RegistrationInviteStatusEnum.Opened, -1, true)]
    [InlineData(RegistrationInviteStatusEnum.Opened, 0, true)]
    [InlineData(RegistrationInviteStatusEnum.Pending, 1, false)]
    [InlineData(RegistrationInviteStatusEnum.Opened, 1, false)]
    [InlineData(RegistrationInviteStatusEnum.Completed, -1, false)]
    [InlineData(RegistrationInviteStatusEnum.Expired, -1, false)]
    [InlineData(RegistrationInviteStatusEnum.Canceled, -1, false)]
    public void Expiration_UsesDeadlineForPendingAndOpenedInvites(
        RegistrationInviteStatusEnum status,
        int offsetSeconds,
        bool expected)
    {
        var now = new DateTime(2026, 8, 29, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            expected,
            PeopleManagementController.ShouldExpireInvite(status, now.AddSeconds(offsetSeconds), now));
    }
}

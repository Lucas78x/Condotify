using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Mobile;
using CondotifyAPI.Domain.Enums.Mobile;
using Microsoft.AspNetCore.Authorization;

namespace CondotifyAPI.Tests;

public sealed class MobileNotificationsControllerTests
{
    [Fact]
    public void Controller_AcceptsOnlyTheNamedAnyPrincipalPolicy()
    {
        var attribute = Assert.Single(typeof(MobileNotificationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(SessionController.AnyPrincipalPolicy, attribute.Policy);
    }

    [Theory]
    [InlineData(MobileNotificationCategory.Security)]
    [InlineData(MobileNotificationCategory.System)]
    public void IsEssential_ProtectsSecurityAndSystem(MobileNotificationCategory category) =>
        Assert.True(MobileNotificationsController.IsEssential(category));

    [Theory]
    [InlineData(MobileNotificationCategory.Access)]
    [InlineData(MobileNotificationCategory.Visitor)]
    [InlineData(MobileNotificationCategory.Delivery)]
    [InlineData(MobileNotificationCategory.Booking)]
    [InlineData(MobileNotificationCategory.Operational)]
    public void IsEssential_AllowsOptionalCategories(MobileNotificationCategory category) =>
        Assert.False(MobileNotificationsController.IsEssential(category));

    [Fact]
    public void ValidateInstallation_AcceptsValidAndroidRegistration()
    {
        var input = new MobileInstallationUpsertIn
        {
            Platform = MobilePlatform.Android,
            PushToken = new string('t', 80)
        };

        Assert.Null(MobileNotificationsController.ValidateInstallation("firebase-installation-id", input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateInstallation_RejectsBlankInstallationId(string installationId)
    {
        var input = new MobileInstallationUpsertIn
        {
            Platform = MobilePlatform.Android,
            PushToken = new string('t', 80)
        };

        Assert.NotNull(MobileNotificationsController.ValidateInstallation(installationId, input));
    }

    [Fact]
    public void ValidateInstallation_RejectsShortTokenAndUnknownPlatform()
    {
        var input = new MobileInstallationUpsertIn
        {
            Platform = MobilePlatform.Unknown,
            PushToken = "short"
        };

        Assert.NotNull(MobileNotificationsController.ValidateInstallation("installation", input));
    }
}

using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Mobile;

namespace CondotifyAPI.Tests;

public sealed class PushNotificationWorkerTests
{
    [Theory]
    [InlineData(MobileNotificationCategory.Security, false, true)]
    [InlineData(MobileNotificationCategory.System, false, true)]
    [InlineData(MobileNotificationCategory.Visitor, false, false)]
    [InlineData(MobileNotificationCategory.Visitor, true, true)]
    [InlineData(MobileNotificationCategory.Visitor, null, true)]
    public void PreferenceAllows_ProtectsEssentialCategories(
        MobileNotificationCategory category,
        bool? enabled,
        bool expected) =>
        Assert.Equal(expected, PushNotificationWorker.PreferenceAllows(category, enabled));

    [Fact]
    public void ApplyResult_Delivered_CompletesDelivery()
    {
        var (delivery, installation) = CreateDelivery();
        var now = DateTime.UtcNow;

        PushNotificationWorker.ApplyResult(
            delivery,
            installation,
            PushTransportResult.Delivered("projects/demo/messages/1"),
            now);

        Assert.Equal(PushDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(now, delivery.FinishedAt);
        Assert.Equal("projects/demo/messages/1", delivery.ProviderMessageId);
        Assert.True(installation.IsActive);
    }

    [Fact]
    public void ApplyResult_InvalidToken_RetiresInstallation()
    {
        var (delivery, installation) = CreateDelivery();
        var originalHash = installation.TokenHash;
        var now = DateTime.UtcNow;

        PushNotificationWorker.ApplyResult(
            delivery,
            installation,
            new PushTransportResult(PushTransportOutcome.InvalidToken, 404, string.Empty, "UNREGISTERED"),
            now);

        Assert.Equal(PushDeliveryStatus.Cancelled, delivery.Status);
        Assert.False(installation.IsActive);
        Assert.Empty(installation.PushToken);
        Assert.NotEqual(originalHash, installation.TokenHash);
    }

    [Fact]
    public void ApplyResult_TransientFailure_SchedulesRetry()
    {
        var (delivery, installation) = CreateDelivery();
        delivery.AttemptCount = 2;
        var now = DateTime.UtcNow;

        PushNotificationWorker.ApplyResult(
            delivery,
            installation,
            new PushTransportResult(PushTransportOutcome.TransientFailure, 503, string.Empty, "unavailable"),
            now);

        Assert.Equal(PushDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(now.AddMinutes(4), delivery.NextAttemptAt);
        Assert.Null(delivery.FinishedAt);
    }

    [Fact]
    public void ApplyResult_FinalTransientFailure_DeadLettersDelivery()
    {
        var (delivery, installation) = CreateDelivery();
        delivery.AttemptCount = delivery.MaxAttempts;

        PushNotificationWorker.ApplyResult(
            delivery,
            installation,
            new PushTransportResult(PushTransportOutcome.TransientFailure, 503, string.Empty, "unavailable"),
            DateTime.UtcNow);

        Assert.Equal(PushDeliveryStatus.DeadLetter, delivery.Status);
        Assert.NotNull(delivery.FinishedAt);
    }

    [Fact]
    public void Validate_AcceptsResidentAndAllowedRoute()
    {
        var request = Request();

        PushNotificationService.Validate(request);
    }

    [Theory]
    [InlineData("unknown", "/home")]
    [InlineData(PrincipalTypes.Resident, "/admin")]
    public void Validate_RejectsInvalidPrincipalOrRoute(string subjectType, string route)
    {
        var request = Request() with { SubjectType = subjectType, Route = route };

        Assert.Throws<ArgumentException>(() => PushNotificationService.Validate(request));
    }

    private static PushNotificationRequest Request() => new(
        Guid.NewGuid(),
        PrincipalTypes.Resident,
        MobileNotificationCategory.Visitor,
        "Visitante aguardando",
        "Ha uma nova solicitacao.",
        $"/visitors/{Guid.NewGuid():D}",
        $"visitor:{Guid.NewGuid():N}");

    private static (PushDeliveryDTO Delivery, PushInstallationDTO Installation) CreateDelivery()
    {
        var installation = new PushInstallationDTO
        {
            Id = Guid.NewGuid(),
            IsActive = true,
            PushToken = "token",
            TokenHash = PushInstallationTokens.Hash("token")
        };
        return (new PushDeliveryDTO
        {
            Id = Guid.NewGuid(),
            InstallationId = installation.Id,
            Installation = installation,
            Status = PushDeliveryStatus.Sending,
            AttemptCount = 1,
            MaxAttempts = 5
        }, installation);
    }
}

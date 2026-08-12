using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class OfflineAccessEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 15, 0, 0, DateTimeKind.Utc);
    private const string Code = "VIS-0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void AccessCode_NormalizesRawCodeAndInviteUrlToSameHash()
    {
        var raw = OfflineAccessCode.Hash(Code.ToLowerInvariant());
        var url = OfflineAccessCode.Hash($"https://app.condotify.local/convite?code={Uri.EscapeDataString(Code)}");

        Assert.Equal(raw, url);
        Assert.Equal(64, raw.Length);
    }

    [Fact]
    public void BundleAuthenticator_RejectsChangedPayload()
    {
        var secret = Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());
        var envelope = new OfflineAccessBundleEnvelopeViewModel
        {
            KeyId = "device",
            PayloadBase64 = Convert.ToBase64String("payload"u8.ToArray())
        };
        envelope.Signature = OfflineBundleAuthenticator.Sign(envelope.PayloadBase64, secret);

        Assert.True(OfflineBundleAuthenticator.Verify(envelope, secret));
        envelope.PayloadBase64 = Convert.ToBase64String("changed"u8.ToArray());
        Assert.False(OfflineBundleAuthenticator.Verify(envelope, secret));
    }

    [Fact]
    public void Evaluate_AllowsValidPermitOnPrimaryValidator()
    {
        var result = OfflineAccessEvaluator.Evaluate(Bundle(primary: true), Code, Now);

        Assert.True(result.Allowed);
        Assert.Equal("Visitante Teste", result.Permit!.VisitorName);
    }

    [Fact]
    public void Evaluate_DeniesExpiredBundle()
    {
        var bundle = Bundle(primary: true);
        bundle.ExpiresAt = Now.AddMinutes(-1);

        var result = OfflineAccessEvaluator.Evaluate(bundle, Code, Now);

        Assert.Equal(OfflineAccessDecisionCode.BundleExpired, result.Code);
    }

    [Fact]
    public void Evaluate_DeniesLastUseOnSecondaryDevice()
    {
        var result = OfflineAccessEvaluator.Evaluate(Bundle(primary: false), Code, Now);

        Assert.Equal(OfflineAccessDecisionCode.PrimaryValidatorRequired, result.Code);
    }

    [Fact]
    public void Evaluate_DeniesOutsideRouteWindow()
    {
        var bundle = Bundle(primary: true);
        bundle.Visits[0].Routes[0].StartTime = TimeSpan.FromHours(7);
        bundle.Visits[0].Routes[0].EndTime = TimeSpan.FromHours(8);

        var result = OfflineAccessEvaluator.Evaluate(bundle, Code, Now);

        Assert.Equal(OfflineAccessDecisionCode.RouteNotAllowed, result.Code);
    }

    [Fact]
    public void Evaluate_DeniesClockRollback()
    {
        var result = OfflineAccessEvaluator.Evaluate(Bundle(primary: true), Code, Now, clockRollbackDetected: true);

        Assert.Equal(OfflineAccessDecisionCode.ClockInvalid, result.Code);
    }

    [Fact]
    public void Evaluate_DeniesRepeatedLocalUse()
    {
        var bundle = Bundle(primary: true);
        bundle.Visits[0].UseCount = 1;

        var result = OfflineAccessEvaluator.Evaluate(bundle, Code, Now);

        Assert.Equal(OfflineAccessDecisionCode.UsageLimitReached, result.Code);
    }

    private static OfflineAccessBundlePayloadViewModel Bundle(bool primary)
    {
        // 15:00 UTC = 12:00 no horário configurado de São Paulo.
        return new OfflineAccessBundlePayloadViewModel
        {
            BundleId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            LicenseId = Guid.NewGuid(),
            GeneratedAt = Now.AddHours(-1),
            ExpiresAt = Now.AddHours(7),
            ServerTime = Now.AddHours(-1),
            UtcOffsetMinutes = -180,
            IsPrimaryValidator = primary,
            Visits =
            [
                new OfflineVisitPermitViewModel
                {
                    VisitId = Guid.NewGuid(),
                    CodeHash = OfflineAccessCode.Hash(Code),
                    VisitorName = "Visitante Teste",
                    Status = "Scheduled",
                    ValidFrom = Now.AddHours(-2),
                    ValidTo = Now.AddHours(2),
                    MaxUses = 1,
                    Routes =
                    [
                        new OfflineRouteWindowViewModel
                        {
                            RouteId = Guid.NewGuid(), Name = "Portaria",
                            DaysOfWeekMask = 127, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 59)
                        }
                    ]
                }
            ]
        };
    }
}

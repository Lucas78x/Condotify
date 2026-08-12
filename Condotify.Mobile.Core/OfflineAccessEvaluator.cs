using Condotify.Models;

namespace Condotify.Mobile.Core;

public enum OfflineAccessDecisionCode
{
    Allowed = 0,
    InvalidBundle = 1,
    BundleNotActive = 2,
    BundleExpired = 3,
    CodeNotFound = 4,
    VisitNotActive = 5,
    RouteNotAllowed = 6,
    UsageLimitReached = 7,
    PrimaryValidatorRequired = 8,
    ClockInvalid = 9
}

public sealed record OfflineAccessDecision(
    OfflineAccessDecisionCode Code,
    string Message,
    OfflineVisitPermitViewModel? Permit = null)
{
    public bool Allowed => Code == OfflineAccessDecisionCode.Allowed && Permit is not null;
}

public static class OfflineAccessEvaluator
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(2);

    public static OfflineAccessDecision Evaluate(
        OfflineAccessBundlePayloadViewModel? payload,
        string scannedCode,
        DateTime trustedUtc,
        bool clockRollbackDetected = false)
    {
        if (clockRollbackDetected)
            return Deny(OfflineAccessDecisionCode.ClockInvalid, "O relógio do aparelho mudou. Conecte-se para renovar a base segura.");
        if (payload is null || payload.SchemaVersion != 1 || payload.BundleId == Guid.Empty || payload.DeviceId == Guid.Empty)
            return Deny(OfflineAccessDecisionCode.InvalidBundle, "A base offline não é válida.");

        var now = trustedUtc.Kind == DateTimeKind.Utc ? trustedUtc : trustedUtc.ToUniversalTime();
        if (now < payload.GeneratedAt.Add(-ClockTolerance))
            return Deny(OfflineAccessDecisionCode.BundleNotActive, "A base offline ainda não está dentro da janela confiável.");
        if (now > payload.ExpiresAt)
            return Deny(OfflineAccessDecisionCode.BundleExpired, "A base offline venceu. Restabeleça a conexão para sincronizar.");

        var codeHash = OfflineAccessCode.Hash(scannedCode);
        if (codeHash.Length == 0)
            return Deny(OfflineAccessDecisionCode.CodeNotFound, "Leia um QR Code válido.");
        var permit = payload.Visits.FirstOrDefault(x => x.CodeHash.Equals(codeHash, StringComparison.Ordinal));
        if (permit is null)
            return Deny(OfflineAccessDecisionCode.CodeNotFound, "Este convite não está na base segura sincronizada.");
        if (!permit.Status.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
            return Deny(OfflineAccessDecisionCode.VisitNotActive, "A entrada deste convite já foi registrada ou não está mais agendada.");
        if (now < permit.ValidFrom || now > permit.ValidTo)
            return Deny(OfflineAccessDecisionCode.VisitNotActive, "O convite está fora do período autorizado.");
        if (permit.MaxUses.HasValue && permit.UseCount >= permit.MaxUses.Value)
            return Deny(OfflineAccessDecisionCode.UsageLimitReached, "O limite de utilizações deste convite foi atingido.");
        if (permit.MaxUses.HasValue && permit.MaxUses.Value - permit.UseCount <= 1 && !payload.IsPrimaryValidator)
            return Deny(OfflineAccessDecisionCode.PrimaryValidatorRequired, "O último uso disponível exige o validador principal da portaria.");
        if (!OfflineRouteSchedule.IsAllowed(permit.Routes, now, payload.UtcOffsetMinutes))
            return Deny(OfflineAccessDecisionCode.RouteNotAllowed, "O convite está fora dos dias ou horários permitidos.");

        return new OfflineAccessDecision(
            OfflineAccessDecisionCode.Allowed,
            "Autorização validada na base protegida do aparelho.",
            permit);
    }

    private static OfflineAccessDecision Deny(OfflineAccessDecisionCode code, string message) => new(code, message);
}

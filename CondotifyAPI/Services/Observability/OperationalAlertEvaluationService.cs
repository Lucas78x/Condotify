using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Observability;

public interface IOperationalAlertEvaluationService
{
    Task EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class OperationalAlertEvaluationService(
    DatabaseContext context,
    IConfiguration configuration,
    ILogger<OperationalAlertEvaluationService> logger) : IOperationalAlertEvaluationService
{
    private const string AutomaticSource = "MonitorAutomatico";

    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var signals = await CollectSignalsAsync(now, cancellationToken);
        var fingerprints = signals.Select(x => x.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var alerts = await context.OperationalAlerts
            .Where(x => x.Source == AutomaticSource)
            .ToListAsync(cancellationToken);
        var current = alerts.ToDictionary(x => x.Fingerprint, StringComparer.Ordinal);

        foreach (var signal in signals)
        {
            if (!current.TryGetValue(signal.Fingerprint, out var alert))
            {
                context.OperationalAlerts.Add(new OperationalAlertDTO
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = signal.EnterpriseId,
                    LicenseId = signal.LicenseId,
                    Fingerprint = signal.Fingerprint,
                    Type = signal.Type,
                    Source = AutomaticSource,
                    Severity = signal.Severity,
                    Status = OperationalAlertStatus.Open,
                    Title = Short(signal.Title, 180),
                    Message = Short(signal.Message, 1000),
                    TargetUrl = Short(signal.TargetUrl, 300),
                    ResourceType = Short(signal.ResourceType, 80),
                    ResourceId = signal.ResourceId,
                    IsConditionActive = true,
                    OccurrenceCount = 1,
                    FirstOccurredAt = signal.OccurredAt,
                    LastOccurredAt = signal.OccurredAt,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                continue;
            }

            if (!alert.IsConditionActive && alert.Status == OperationalAlertStatus.Resolved)
            {
                alert.Status = OperationalAlertStatus.Open;
                alert.OccurrenceCount++;
                alert.FirstOccurredAt = signal.OccurredAt;
                alert.AcknowledgedAt = null;
                alert.AcknowledgedById = null;
                alert.AcknowledgedBy = string.Empty;
                alert.AcknowledgementNote = string.Empty;
                alert.ResolvedAt = null;
                alert.ResolvedById = null;
                alert.ResolvedBy = string.Empty;
                alert.ResolutionNote = string.Empty;
                alert.SuppressedUntil = null;
                alert.SuppressedById = null;
                alert.SuppressedBy = string.Empty;
                alert.SuppressionReason = string.Empty;
            }

            alert.IsConditionActive = true;
            alert.Severity = signal.Severity;
            alert.Title = Short(signal.Title, 180);
            alert.Message = Short(signal.Message, 1000);
            alert.TargetUrl = Short(signal.TargetUrl, 300);
            alert.ResourceType = Short(signal.ResourceType, 80);
            alert.ResourceId = signal.ResourceId;
            alert.LastOccurredAt = signal.OccurredAt;
            alert.UpdatedAt = now;
        }

        foreach (var alert in alerts.Where(x => x.IsConditionActive && !fingerprints.Contains(x.Fingerprint)))
        {
            alert.IsConditionActive = false;
            if (alert.Status != OperationalAlertStatus.Resolved)
            {
                alert.Status = OperationalAlertStatus.Resolved;
                alert.ResolvedAt = now;
                alert.ResolvedBy = "Sistema";
                alert.ResolutionNote = "A condição voltou ao estado esperado.";
            }
            alert.UpdatedAt = now;
        }

        try
        {
            if (context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Uma avaliacao concorrente de alertas foi descartada");
        }
    }

    private async Task<List<AlertSignal>> CollectSignalsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var signals = new List<AlertSignal>();
        var warningResponseMs = Math.Clamp(
            configuration.GetValue("DeviceMonitoring:ResponseWarningMs", 2500),
            100,
            30000);

        var devices = await context.Devices.AsNoTracking()
            .Where(x => x.LastHealthCheckAt != null && (!x.IsActive || x.LastResponseTimeMs > warningResponseMs))
            .Select(x => new
            {
                x.Id,
                x.LicenseId,
                x.License.EnterpriseId,
                LicenseName = x.License.Name,
                x.Name,
                x.IsActive,
                x.LastHealthCheckAt,
                x.LastResponseTimeMs,
                x.HealthMessage
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(devices.Select(x => !x.IsActive
            ? Signal(
                x.EnterpriseId, x.LicenseId, $"device-offline:{x.Id:N}", "DeviceOffline",
                OperationalAlertSeverity.Critical, $"{x.Name} está offline",
                string.IsNullOrWhiteSpace(x.HealthMessage)
                    ? "O equipamento não respondeu ao monitoramento de conectividade."
                    : x.HealthMessage,
                x.LastHealthCheckAt ?? now, $"/licencas/{x.LicenseId}/equipamentos", "Device", x.Id)
            : Signal(
                x.EnterpriseId, x.LicenseId, $"device-slow:{x.Id:N}", "DeviceSlow",
                OperationalAlertSeverity.Warning, $"{x.Name} está respondendo lentamente",
                $"Tempo de resposta de {x.LastResponseTimeMs} ms; limite configurado em {warningResponseMs} ms.",
                x.LastHealthCheckAt ?? now, $"/licencas/{x.LicenseId}/equipamentos", "Device", x.Id)));

        var offlineRemovals = await context.ResidentAccessDevices.AsNoTracking()
            .Where(x => x.SyncStatus == CredentialSyncStatusEnum.RemovalPending && !x.Device.IsActive)
            .GroupBy(x => new
            {
                LicenseId = x.Credential.Resident.Unit.Block.LicenseId,
                x.Credential.Resident.Unit.Block.License.EnterpriseId,
                x.DeviceId,
                DeviceName = x.Device.Name
            })
            .Select(group => new
            {
                group.Key.LicenseId,
                group.Key.EnterpriseId,
                group.Key.DeviceId,
                group.Key.DeviceName,
                Count = group.Count(),
                OccurredAt = group.Max(x => x.LastSyncAt)
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(offlineRemovals.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"credential-removal-offline:{x.DeviceId:N}", "CredentialRemovalOffline",
            OperationalAlertSeverity.Critical,
            "Remoção temporária aguardando equipamento",
            $"{x.Count} credencial(is) expirada(s) aguardam o equipamento {x.DeviceName} voltar a ficar online.",
            x.OccurredAt, $"/licencas/{x.LicenseId}/credenciais", "Device", x.DeviceId)));

        var failedBindings = await context.ResidentAccessDevices.AsNoTracking()
            .Where(x => x.SyncStatus == CredentialSyncStatusEnum.Failed ||
                        (x.SyncStatus == CredentialSyncStatusEnum.RemovalPending && x.Device.IsActive))
            .GroupBy(x => new
            {
                LicenseId = x.Credential.Resident.Unit.Block.LicenseId,
                x.Credential.Resident.Unit.Block.License.EnterpriseId
            })
            .Select(group => new
            {
                group.Key.LicenseId,
                group.Key.EnterpriseId,
                Count = group.Count(),
                OccurredAt = group.Max(x => x.LastSyncAt)
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(failedBindings.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"credential-sync:{x.LicenseId:N}", "CredentialSyncFailure",
            x.Count >= 10 ? OperationalAlertSeverity.Critical : OperationalAlertSeverity.Warning,
            "Credenciais aguardando sincronização",
            $"{x.Count} vínculo(s) não foram aplicados aos equipamentos.",
            x.OccurredAt, $"/licencas/{x.LicenseId}/credenciais", "License", x.LicenseId)));

        var expiredCredentials = await context.ResidentAccessCredentials.AsNoTracking()
            .Where(x => x.IsActive && x.ValidTo < now)
            .GroupBy(x => new
            {
                LicenseId = x.Resident.Unit.Block.LicenseId,
                x.Resident.Unit.Block.License.EnterpriseId
            })
            .Select(group => new
            {
                group.Key.LicenseId,
                group.Key.EnterpriseId,
                Count = group.Count(),
                OccurredAt = group.Max(x => x.ValidTo)
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(expiredCredentials.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"credentials-expired:{x.LicenseId:N}", "CredentialExpired",
            OperationalAlertSeverity.Warning, "Credenciais fora da validade",
            $"{x.Count} credencial(is) ainda estão ativas após o vencimento.",
            x.OccurredAt, $"/licencas/{x.LicenseId}/credenciais", "License", x.LicenseId)));

        var failedBatches = await context.AccessBatchOperations.AsNoTracking()
            .Where(x => x.Status == AccessBatchStatusEnum.Failed ||
                        x.Status == AccessBatchStatusEnum.DeadLetter ||
                        x.Status == AccessBatchStatusEnum.CompletedWithErrors)
            .Select(x => new
            {
                x.Id,
                x.LicenseId,
                x.License.EnterpriseId,
                x.Status,
                x.FailedItems,
                x.Error,
                x.CreatedAt,
                x.FinishedAt
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(failedBatches.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"batch-failure:{x.Id:N}", "BatchFailure",
            x.Status == AccessBatchStatusEnum.DeadLetter
                ? OperationalAlertSeverity.Critical
                : OperationalAlertSeverity.Warning,
            "Operação em lote requer atenção",
            string.IsNullOrWhiteSpace(x.Error)
                ? $"{x.FailedItems} item(ns) terminaram com erro."
                : x.Error,
            x.FinishedAt ?? x.CreatedAt, $"/licencas/{x.LicenseId}/credenciais", "AccessBatch", x.Id)));

        var backupFailures = await context.BackupAutomationPolicies.AsNoTracking()
            .Where(x => x.Enabled && x.LastError != "")
            .Select(x => new
            {
                x.LicenseId,
                x.License.EnterpriseId,
                x.LastError,
                x.LastRunAt,
                x.UpdatedAt
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(backupFailures.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"backup-failure:{x.LicenseId:N}", "BackupFailure",
            OperationalAlertSeverity.Critical, "Backup automático com falha",
            x.LastError, x.LastRunAt ?? x.UpdatedAt,
            $"/licencas/{x.LicenseId}/administracao", "BackupAutomation", x.LicenseId)));

        var overdueBackups = await context.BackupAutomationPolicies.AsNoTracking()
            .Where(x => x.Enabled && x.LastError == "" && x.NextRunAt != null && x.NextRunAt < now.AddHours(-1))
            .Select(x => new { x.LicenseId, x.License.EnterpriseId, x.NextRunAt })
            .ToListAsync(cancellationToken);
        signals.AddRange(overdueBackups.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"backup-overdue:{x.LicenseId:N}", "BackupOverdue",
            OperationalAlertSeverity.Warning, "Backup automático atrasado",
            "A execução programada não foi concluída dentro da janela esperada.",
            x.NextRunAt ?? now, $"/licencas/{x.LicenseId}/administracao", "BackupAutomation", x.LicenseId)));

        var pendingBookings = await context.AmenityBookings.AsNoTracking()
            .Where(x => x.Status == AmenityBookingStatusEnum.Pending)
            .GroupBy(x => new { x.LicenseId, x.License.EnterpriseId })
            .Select(group => new
            {
                group.Key.LicenseId,
                group.Key.EnterpriseId,
                Count = group.Count(),
                OccurredAt = group.Min(x => x.CreatedAt)
            })
            .ToListAsync(cancellationToken);
        signals.AddRange(pendingBookings.Select(x => Signal(
            x.EnterpriseId, x.LicenseId, $"bookings-pending:{x.LicenseId:N}", "BookingApproval",
            OperationalAlertSeverity.Info, "Reservas aguardando aprovação",
            $"{x.Count} solicitação(ões) precisam de análise.",
            x.OccurredAt, $"/licencas/{x.LicenseId}/agendamento", "License", x.LicenseId)));

        return signals;
    }

    private static AlertSignal Signal(
        Guid enterpriseId,
        Guid licenseId,
        string fingerprint,
        string type,
        OperationalAlertSeverity severity,
        string title,
        string message,
        DateTime occurredAt,
        string targetUrl,
        string resourceType,
        Guid resourceId) =>
        new(
            enterpriseId,
            licenseId,
            fingerprint,
            type,
            severity,
            title,
            message,
            occurredAt.Kind == DateTimeKind.Utc ? occurredAt : occurredAt.ToUniversalTime(),
            targetUrl,
            resourceType,
            resourceId);

    private static string Short(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record AlertSignal(
        Guid EnterpriseId,
        Guid LicenseId,
        string Fingerprint,
        string Type,
        OperationalAlertSeverity Severity,
        string Title,
        string Message,
        DateTime OccurredAt,
        string TargetUrl,
        string ResourceType,
        Guid ResourceId);
}

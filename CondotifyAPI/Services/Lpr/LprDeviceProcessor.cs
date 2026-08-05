using AutoMapper;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.CFTV;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public sealed class LprDeviceProcessor(
    ICftvSnapshotService snapshotService,
    ILprRecognitionClient recognitionClient,
    IVehicleLookupService vehicleLookup,
    IAccessControlService accessControl,
    IMapper mapper,
    ILprDebounceStore debounceStore,
    IConfiguration configuration,
    ILogger<LprDeviceProcessor> logger)
{
    // Sentinel debounce key (distinct from any real, normalized plate) used to
    // rate-limit "nothing to report" audit rows - camera/snapshot/OCR failures
    // and genuinely empty reads - so a quiet gate doesn't write a NoRead row
    // every poll cycle (would be ~43k rows/device/day at a 2s cadence).
    private const string NoPlateDebounceKey = "";

    private TimeSpan DebounceWindow =>
        TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Lpr:DebounceSeconds", 20), 1, 300));

    public async Task ProcessAsync(DatabaseContext context, AccessControlDeviceDTO device, CancellationToken cancellationToken)
    {
        if (device.LprMode is null || device.LprCameraId is null) return;

        var camera = await context.CFTVDevices.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == device.LprCameraId, cancellationToken);
        if (camera == null)
        {
            logger.LogWarning("Cancela {DeviceId} aponta para uma camera LPR inexistente {CameraId}.", device.Id, device.LprCameraId);
            await TryRecordNoReadAsync(context, device, confidence: 0.0, cancellationToken);
            return;
        }

        CftvSnapshot? snapshot;
        try
        {
            snapshot = await snapshotService.FetchAsync(camera, device.LprCameraChannel ?? 1, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao capturar snapshot da camera {CameraId} para LPR.", camera.Id);
            await TryRecordNoReadAsync(context, device, confidence: 0.0, cancellationToken);
            return;
        }

        if (snapshot == null)
        {
            await TryRecordNoReadAsync(context, device, confidence: 0.0, cancellationToken);
            return;
        }

        PlateRecognitionResult recognition;
        try
        {
            recognition = await recognitionClient.RecognizeAsync(snapshot.Content, snapshot.ContentType, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Servico de OCR indisponivel ao processar cancela {DeviceId}.", device.Id);
            await TryRecordNoReadAsync(context, device, confidence: 0.0, cancellationToken);
            return;
        }

        var confidenceThreshold = Math.Clamp(configuration.GetValue("Lpr:ConfidenceThreshold", 0.75), 0.0, 1.0);
        var normalizedPlate = PlateNormalizer.Normalize(recognition.Plate);

        if (normalizedPlate == null)
        {
            // OCR ran successfully but found no plate (empty frame, no vehicle
            // present, etc). Genuinely "nothing happening" - same debounce
            // treatment as the failure paths above.
            await TryRecordNoReadAsync(context, device, recognition.Confidence, cancellationToken);
            return;
        }

        if (debounceStore.WasRecentlyTriggered(device.Id, normalizedPlate, DebounceWindow))
            return; // Already handled this exact plate recently - silent skip, no audit.

        var matchedVehicleId = await vehicleLookup.FindActiveVehicleIdAsync(device.LicenseId, normalizedPlate, cancellationToken);

        var action = LprDecisionEngine.Decide(
            plateWasRead: true,
            recognition.Confidence,
            confidenceThreshold,
            matchedVehicleId.HasValue,
            device.LprMode.Value);

        // Mark this plate as handled regardless of outcome - including a
        // NoRead caused by confidence below threshold. Otherwise a vehicle
        // idling at the barrier with a plate that keeps reading just under
        // the threshold would never trip the early debounce check above,
        // and would get a fresh audit row every poll indefinitely.
        debounceStore.MarkTriggered(device.Id, normalizedPlate);

        var audit = new VehicleAccessAuditDTO
        {
            Id = Guid.NewGuid(),
            AccessControlDeviceId = device.Id,
            PlateRead = normalizedPlate,
            Confidence = recognition.Confidence,
            MatchedVehicleId = matchedVehicleId,
            Action = action switch
            {
                LprAction.Opened => VehicleAccessAuditAction.Opened,
                LprAction.AlertRaised => VehicleAccessAuditAction.AlertRaised,
                LprAction.DetectedOnly => VehicleAccessAuditAction.DetectedOnly,
                _ => VehicleAccessAuditAction.NoRead
            },
            Timestamp = DateTime.UtcNow
        };
        context.VehicleAccessAudits.Add(audit);

        if (action == LprAction.AlertRaised)
        {
            await RaiseAlertAsync(
                context,
                device,
                alertType: "LprPlateNotRecognized",
                plate: normalizedPlate,
                title: $"Veiculo nao identificado em {device.Name}",
                message: $"Placa {normalizedPlate} nao possui cadastro ativo para a cancela {device.Name}.",
                cancellationToken);
        }

        // Persist the audit (and any alert) before touching hardware. This is
        // our best-known state at this point - Action reflects exactly what
        // LprDecisionEngine.Decide returned. If the process crashes between
        // here and the physical door command below, there's still a durable
        // record instead of nothing at all.
        await context.SaveChangesAsync(cancellationToken);

        if (action == LprAction.Opened)
        {
            var opened = false;
            try
            {
                opened = await accessControl.OpenDoorAsync(mapper.Map<AccessControlDevice>(device), device.LprDoorChannel ?? 1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao abrir a cancela {DeviceId} apos leitura de placa por LPR.", device.Id);
            }

            if (!opened)
            {
                // The decision engine authorized this vehicle but the
                // physical open failed (driver returned false, or threw).
                // Correct the audit in place - it must not claim Opened when
                // the gate never moved - and raise an alert so a porter knows
                // an authorized vehicle is stuck outside.
                audit.Action = VehicleAccessAuditAction.AlertRaised;

                await RaiseAlertAsync(
                    context,
                    device,
                    alertType: "LprGateOpenFailed",
                    plate: normalizedPlate,
                    title: $"Falha ao abrir a cancela {device.Name}",
                    message: $"A cancela {device.Name} reconheceu a placa {normalizedPlate} mas nao conseguiu abrir automaticamente.",
                    cancellationToken);

                await context.SaveChangesAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Writes a debounced NoRead audit row for "nothing to report" cases
    /// (missing camera, snapshot failure, OCR failure, or a genuinely empty
    /// read). Uses a sentinel debounce key distinct from real plates so a
    /// quiet gate produces at most one row per debounce window instead of
    /// one per poll cycle.
    /// </summary>
    private async Task TryRecordNoReadAsync(DatabaseContext context, AccessControlDeviceDTO device, double confidence, CancellationToken cancellationToken)
    {
        if (debounceStore.WasRecentlyTriggered(device.Id, NoPlateDebounceKey, DebounceWindow))
            return;

        debounceStore.MarkTriggered(device.Id, NoPlateDebounceKey);

        context.VehicleAccessAudits.Add(new VehicleAccessAuditDTO
        {
            Id = Guid.NewGuid(),
            AccessControlDeviceId = device.Id,
            PlateRead = null,
            Confidence = confidence,
            MatchedVehicleId = null,
            Action = VehicleAccessAuditAction.NoRead,
            Timestamp = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Upserts an operational alert by fingerprint (device + alert type +
    /// plate), incrementing the occurrence count if one is already open
    /// rather than spamming a new alert per poll cycle.
    /// </summary>
    private static async Task RaiseAlertAsync(
        DatabaseContext context,
        AccessControlDeviceDTO device,
        string alertType,
        string plate,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        var license = await context.Licenses.AsNoTracking()
            .FirstAsync(l => l.Id == device.LicenseId, cancellationToken);
        var fingerprint = $"lpr:{alertType}:{device.Id}:{plate}";
        var now = DateTime.UtcNow;

        var existing = await context.OperationalAlerts
            .FirstOrDefaultAsync(a => a.EnterpriseId == license.EnterpriseId && a.Fingerprint == fingerprint, cancellationToken);

        if (existing != null)
        {
            existing.OccurrenceCount++;
            existing.LastOccurredAt = now;
            existing.IsConditionActive = true;
            existing.Status = OperationalAlertStatus.Open;
            return;
        }

        context.OperationalAlerts.Add(new OperationalAlertDTO
        {
            Id = Guid.NewGuid(),
            EnterpriseId = license.EnterpriseId,
            LicenseId = license.Id,
            Fingerprint = fingerprint,
            Type = alertType,
            Source = "Lpr",
            Severity = OperationalAlertSeverity.Warning,
            Status = OperationalAlertStatus.Open,
            Title = title,
            Message = message,
            ResourceType = "AccessControlDevice",
            ResourceId = device.Id,
            IsConditionActive = true,
            OccurrenceCount = 1,
            FirstOccurredAt = now,
            LastOccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}

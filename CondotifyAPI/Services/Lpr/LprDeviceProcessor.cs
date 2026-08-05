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
    public async Task ProcessAsync(DatabaseContext context, AccessControlDeviceDTO device, CancellationToken cancellationToken)
    {
        if (device.LprMode is null || device.LprCameraId is null) return;

        var camera = await context.CFTVDevices.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == device.LprCameraId, cancellationToken);
        if (camera == null)
        {
            logger.LogWarning("Cancela {DeviceId} aponta para uma camera LPR inexistente {CameraId}.", device.Id, device.LprCameraId);
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
            return;
        }

        if (snapshot == null) return;

        PlateRecognitionResult recognition;
        try
        {
            recognition = await recognitionClient.RecognizeAsync(snapshot.Content, snapshot.ContentType, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Servico de OCR indisponivel ao processar cancela {DeviceId}.", device.Id);
            return;
        }

        var confidenceThreshold = Math.Clamp(configuration.GetValue("Lpr:ConfidenceThreshold", 0.75), 0.0, 1.0);
        var normalizedPlate = PlateNormalizer.Normalize(recognition.Plate);
        var plateWasRead = normalizedPlate != null;

        if (plateWasRead)
        {
            var debounceSeconds = Math.Clamp(configuration.GetValue("Lpr:DebounceSeconds", 20), 1, 300);
            if (debounceStore.WasRecentlyTriggered(device.Id, normalizedPlate!, TimeSpan.FromSeconds(debounceSeconds)))
                return;
        }

        Guid? matchedVehicleId = plateWasRead
            ? await vehicleLookup.FindActiveVehicleIdAsync(device.LicenseId, normalizedPlate!, cancellationToken)
            : null;

        var action = LprDecisionEngine.Decide(
            plateWasRead,
            recognition.Confidence,
            confidenceThreshold,
            matchedVehicleId.HasValue,
            device.LprMode.Value);

        if (plateWasRead && action != LprAction.NoRead)
            debounceStore.MarkTriggered(device.Id, normalizedPlate!);

        context.VehicleAccessAudits.Add(new VehicleAccessAuditDTO
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
        });

        if (action == LprAction.Opened)
        {
            try
            {
                await accessControl.OpenDoorAsync(mapper.Map<AccessControlDevice>(device), device.LprCameraChannel ?? 1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao abrir a cancela {DeviceId} apos leitura de placa por LPR.", device.Id);
            }
        }
        else if (action == LprAction.AlertRaised)
        {
            await RaiseAlertAsync(context, device, normalizedPlate, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task RaiseAlertAsync(DatabaseContext context, AccessControlDeviceDTO device, string? plate, CancellationToken cancellationToken)
    {
        var license = await context.Licenses.AsNoTracking()
            .FirstAsync(l => l.Id == device.LicenseId, cancellationToken);
        var fingerprint = $"lpr:{device.Id}:{plate}";
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
            Type = "LprPlateNotRecognized",
            Source = "Lpr",
            Severity = OperationalAlertSeverity.Warning,
            Status = OperationalAlertStatus.Open,
            Title = $"Veiculo nao identificado em {device.Name}",
            Message = plate is null
                ? $"A cancela {device.Name} nao conseguiu ler a placa do veiculo com confianca suficiente."
                : $"Placa {plate} nao possui cadastro ativo para a cancela {device.Name}.",
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

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoMapper;
using CondotifyAPI.Data.Backups;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Backups;

public interface IConfigurationBackupService
{
    Task<ConfigurationBackupDTO> CreateAsync(
        Guid licenseId,
        string name,
        string description,
        string actor,
        CancellationToken cancellationToken);

    Task<ConfigurationBackupDTO> ImportAsync(
        Guid licenseId,
        ImportedConfigurationBackup imported,
        string actor,
        CancellationToken cancellationToken);

    Task<ConfigurationRestorePreviewOut?> PreviewAsync(
        Guid licenseId,
        Guid backupId,
        PreviewConfigurationRestoreIn input,
        CancellationToken cancellationToken);

    Task<List<ConfigurationBackupTargetOut>?> GetTargetsAsync(
        Guid licenseId,
        Guid backupId,
        CancellationToken cancellationToken);

    Task<ConfigurationRestoreExecutionOut?> RestoreAsync(
        Guid licenseId,
        Guid backupId,
        ExecuteConfigurationRestoreIn input,
        string actor,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationBackupService(
    DatabaseContext context,
    IAccessControlService accessControl,
    IMapper mapper) : IConfigurationBackupService
{
    public const int FormatVersion = 1;
    public const int MaxVersionsPerLicense = 50;
    public const string RestoreConfirmation = "RESTAURAR";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ConfigurationBackupDTO> CreateAsync(
        Guid licenseId,
        string name,
        string description,
        string actor,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var payload = await CaptureAsync(licenseId, cancellationToken);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var now = DateTime.UtcNow;

        var version = (await context.ConfigurationBackups
            .Where(x => x.LicenseId == licenseId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var oldVersions = await context.ConfigurationBackups
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.Version)
            .Skip(MaxVersionsPerLicense - 1)
            .ToListAsync(cancellationToken);
        if (oldVersions.Count > 0)
            context.ConfigurationBackups.RemoveRange(oldVersions);
        var backup = new ConfigurationBackupDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Version = version,
            Name = NormalizeName(name, version),
            Description = Short(description.Trim(), 500),
            PayloadJson = payloadJson,
            Checksum = Checksum(payloadJson),
            DeviceCount = payload.Devices.Count,
            RouteCount = payload.Routes.Count,
            CredentialCount = payload.Credentials.Count,
            BindingCount = payload.Bindings.Count,
            OverrideCount = payload.RouteOverrides.Count,
            CreatedBy = Short(actor, 150),
            CreatedAt = now
        };
        context.ConfigurationBackups.Add(backup);
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "ConfigurationBackup",
            EntityId = backup.Id,
            Action = "Created",
            Status = "Success",
            Summary = $"Backup v{version} criado com {backup.CredentialCount} credencial(is), {backup.RouteCount} rota(s) e {backup.DeviceCount} equipamento(s).",
            DetailsJson = JsonSerializer.Serialize(new
            {
                backup.Version,
                backup.DeviceCount,
                backup.RouteCount,
                backup.CredentialCount,
                backup.BindingCount,
                backup.OverrideCount,
                RemovedOldVersions = oldVersions.Count
            }),
            UserName = actor,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return backup;
    }

    public async Task<ConfigurationBackupDTO> ImportAsync(
        Guid licenseId,
        ImportedConfigurationBackup imported,
        string actor,
        CancellationToken cancellationToken)
    {
        if (imported.LicenseId != licenseId)
            throw new ConfigurationRestoreException("InvalidArchive", "O arquivo pertence a outra licenca.");
        var payload = ParsePayload(licenseId, imported.PayloadJson, imported.PayloadChecksum);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var version = (await context.ConfigurationBackups
            .Where(x => x.LicenseId == licenseId)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var oldVersions = await context.ConfigurationBackups
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.Version)
            .Skip(MaxVersionsPerLicense - 1)
            .ToListAsync(cancellationToken);
        if (oldVersions.Count > 0)
            context.ConfigurationBackups.RemoveRange(oldVersions);
        var now = DateTime.UtcNow;
        var backup = new ConfigurationBackupDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Version = version,
            Name = Short($"Importado - {imported.Name}", 120),
            Description = Short(imported.Description, 500),
            PayloadJson = imported.PayloadJson,
            Checksum = imported.PayloadChecksum,
            DeviceCount = payload.Devices.Count,
            RouteCount = payload.Routes.Count,
            CredentialCount = payload.Credentials.Count,
            BindingCount = payload.Bindings.Count,
            OverrideCount = payload.RouteOverrides.Count,
            CreatedBy = Short(actor, 150),
            CreatedAt = now
        };
        context.ConfigurationBackups.Add(backup);
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "ConfigurationBackup",
            EntityId = backup.Id,
            Action = "Imported",
            Status = "Success",
            Summary = $"Arquivo externo v{imported.SourceVersion} importado como backup v{version}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                imported.SourceVersion,
                imported.ExportedAt,
                backup.DeviceCount,
                backup.RouteCount,
                backup.CredentialCount,
                RemovedOldVersions = oldVersions.Count
            }),
            UserName = actor,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return backup;
    }

    public async Task<ConfigurationRestorePreviewOut?> PreviewAsync(
        Guid licenseId,
        Guid backupId,
        PreviewConfigurationRestoreIn input,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPayloadAsync(licenseId, backupId, cancellationToken);
        return loaded is null
            ? null
            : await BuildPreviewAsync(loaded.Value.Backup, loaded.Value.Payload, input, cancellationToken);
    }

    public async Task<List<ConfigurationBackupTargetOut>?> GetTargetsAsync(
        Guid licenseId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPayloadAsync(licenseId, backupId, cancellationToken);
        if (loaded is null) return null;
        var payload = loaded.Value.Payload;
        return payload.Devices
            .OrderBy(x => x.Name)
            .Select(device => new ConfigurationBackupTargetOut
            {
                DeviceId = device.Id,
                Name = device.Name,
                Model = device.Model,
                Type = device.Type.ToString(),
                IsActive = device.IsActive,
                CredentialCount = payload.Bindings
                    .Where(x => x.DeviceId == device.Id)
                    .Select(x => x.ResidentAccessCredentialId)
                    .Distinct()
                    .Count(),
                RouteCount = payload.RouteDevices
                    .Where(x => x.DeviceId == device.Id)
                    .Select(x => x.AccessRouteId)
                    .Distinct()
                    .Count()
            })
            .ToList();
    }

    public async Task<ConfigurationRestoreExecutionOut?> RestoreAsync(
        Guid licenseId,
        Guid backupId,
        ExecuteConfigurationRestoreIn input,
        string actor,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var loaded = await LoadPayloadAsync(licenseId, backupId, cancellationToken);
        if (loaded is null) return null;
        if (!string.Equals(input.Confirmation.Trim(), RestoreConfirmation, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationRestoreException("ConfirmationRequired", $"Digite {RestoreConfirmation} para confirmar a restauracao.");

        var backup = loaded.Value.Backup;
        var payload = loaded.Value.Payload;
        // Revalida o estado central dentro da transação, sem manter a transação
        // aberta durante chamadas de rede aos equipamentos. A comparação ao vivo
        // pertence à etapa de simulação e a reconciliação posterior trata deriva.
        var executionValidation = new PreviewConfigurationRestoreIn
        {
            Mode = input.Mode,
            IncludeDevices = input.IncludeDevices,
            IncludeRoutes = input.IncludeRoutes,
            IncludeCredentials = input.IncludeCredentials,
            CompareWithEquipment = false,
            TargetDeviceIds = input.TargetDeviceIds.ToList()
        };
        var preview = await BuildPreviewAsync(backup, payload, executionValidation, cancellationToken);
        if (!preview.CanRestore)
            throw new ConfigurationRestoreException(
                "RestoreConflict",
                preview.Conflicts.Count > 0
                    ? string.Join(" ", preview.Conflicts.Take(5))
                    : "A simulacao encontrou conflitos que impedem a restauracao.");

        var mode = NormalizeMode(input.Mode);
        var now = DateTime.UtcNow;
        var snapshotDeviceIds = payload.Devices.Select(x => x.Id).ToHashSet();
        var targetDeviceIds = input.TargetDeviceIds.Count == 0
            ? snapshotDeviceIds
            : input.TargetDeviceIds.Where(snapshotDeviceIds.Contains).ToHashSet();
        var isMassRestore = targetDeviceIds.SetEquals(snapshotDeviceIds);

        if (input.IncludeDevices)
            await RestoreDevicesAsync(licenseId, payload, targetDeviceIds, isMassRestore, mode, now, cancellationToken);
        if (input.IncludeRoutes)
            await RestoreRoutesAsync(licenseId, payload, targetDeviceIds, isMassRestore, mode, now, cancellationToken);
        if (input.IncludeCredentials)
            await RestoreCredentialsAsync(licenseId, payload, targetDeviceIds, isMassRestore, mode, now, cancellationToken);

        Guid? batchId = null;
        var credentialsQueued = 0;
        if (input.IncludeDevices || input.IncludeRoutes || input.IncludeCredentials)
        {
            var credentialIds = isMassRestore
                ? input.IncludeDevices || input.IncludeRoutes
                    ? await context.ResidentAccessCredentials
                        .ForLicense(licenseId)
                        .Select(x => x.Id)
                        .ToListAsync(cancellationToken)
                    : payload.Credentials.Select(x => x.Id).Distinct().ToList()
                : payload.Bindings
                    .Where(x => targetDeviceIds.Contains(x.DeviceId))
                    .Select(x => x.ResidentAccessCredentialId)
                    .Distinct()
                    .ToList();
            credentialsQueued = credentialIds.Count;
            if (credentialIds.Count > 0)
            {
                batchId = Guid.NewGuid();
                context.AccessBatchOperations.Add(new AccessBatchOperationDTO
                {
                    Id = batchId.Value,
                    LicenseId = licenseId,
                    Operation = "ReconcileCredentials",
                    IdempotencyKey = $"backup:{backup.Id:N}:{now.Ticks}",
                    Status = AccessBatchStatusEnum.Queued,
                    RequestedBy = actor,
                    FilterJson = JsonSerializer.Serialize(new { credentialIds, backupId = backup.Id, backup.Version }),
                    Priority = 80,
                    CreatedAt = now
                });
            }
        }

        backup.LastRestoredAt = now;
        backup.LastRestoredBy = Short(actor, 150);
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "ConfigurationBackup",
            EntityId = backup.Id,
            Action = "Restored",
            Status = credentialsQueued > 0 ? "Queued" : "Success",
            Summary = $"Backup v{backup.Version} restaurado no modo {ModeLabel(mode)}.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                backup.Version,
                Mode = mode,
                input.IncludeDevices,
                input.IncludeRoutes,
                input.IncludeCredentials,
                TargetDeviceIds = targetDeviceIds,
                IsMassRestore = isMassRestore,
                preview.CreateCount,
                preview.UpdateCount,
                preview.DeactivateCount,
                CredentialsQueued = credentialsQueued,
                ReconciliationBatchId = batchId
            }),
            UserName = actor,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ConfigurationRestoreExecutionOut
        {
            BackupId = backup.Id,
            Version = backup.Version,
            Mode = mode,
            CreatedCount = preview.CreateCount,
            UpdatedCount = preview.UpdateCount,
            DeactivatedCount = preview.DeactivateCount,
            CredentialsQueued = credentialsQueued,
            TargetDeviceCount = targetDeviceIds.Count,
            IsMassRestore = isMassRestore,
            ReconciliationBatchId = batchId,
            RestoredAt = now,
            Message = credentialsQueued > 0
                ? $"Configuracoes restauradas. {credentialsQueued} credencial(is) foram enfileiradas para sincronizacao."
                : "Configuracoes restauradas com sucesso."
        };
    }

    private async Task<BackupPayload> CaptureAsync(Guid licenseId, CancellationToken cancellationToken)
    {
        var devices = await context.Devices.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var routes = await context.AccessRoutes.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var routeIds = routes.Select(x => x.Id).ToList();
        var routeDevices = await context.AccessRouteDevices.AsNoTracking()
            .Where(x => routeIds.Contains(x.AccessRouteId))
            .ToListAsync(cancellationToken);
        var credentials = await context.ResidentAccessCredentials.AsNoTracking()
            .ForLicense(licenseId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var credentialIds = credentials.Select(x => x.Id).ToList();
        var bindings = await context.ResidentAccessDevices.AsNoTracking()
            .Where(x => credentialIds.Contains(x.ResidentAccessCredentialId))
            .ToListAsync(cancellationToken);
        var overrides = await context.AccessRouteResidentOverrides.AsNoTracking()
            .Where(x => routeIds.Contains(x.AccessRouteId))
            .ToListAsync(cancellationToken);
        var policy = await context.LicenseCredentialPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        var notificationPolicy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);

        return new BackupPayload(
            FormatVersion,
            licenseId,
            DateTime.UtcNow,
            devices.Select(DeviceSnapshot.From).ToList(),
            routes.Select(RouteSnapshot.From).ToList(),
            routeDevices.Select(RouteDeviceSnapshot.From).ToList(),
            credentials.Select(CredentialSnapshot.From).ToList(),
            bindings.Select(CredentialBindingSnapshot.From).ToList(),
            overrides.Select(RouteOverrideSnapshot.From).ToList(),
            policy is null ? null : CredentialPolicySnapshot.From(policy),
            notificationPolicy is null ? null : AlertNotificationPolicySnapshot.From(notificationPolicy));
    }

    private async Task<ConfigurationRestorePreviewOut> BuildPreviewAsync(
        ConfigurationBackupDTO backup,
        BackupPayload payload,
        PreviewConfigurationRestoreIn input,
        CancellationToken cancellationToken)
    {
        var mode = NormalizeMode(input.Mode);
        var sections = new List<ConfigurationRestoreSectionOut>();
        var report = new List<ConfigurationRestoreConflictOut>();
        var requestedDeviceIds = input.TargetDeviceIds.Distinct().ToHashSet();
        var snapshotDeviceIds = payload.Devices.Select(x => x.Id).ToHashSet();
        var targetDeviceIds = requestedDeviceIds.Count == 0
            ? snapshotDeviceIds
            : requestedDeviceIds.Where(snapshotDeviceIds.Contains).ToHashSet();
        var isMassRestore = targetDeviceIds.SetEquals(snapshotDeviceIds);

        void AddConflict(
            string code,
            string section,
            string message,
            string suggestedAction,
            Guid? entityId = null,
            string entityName = "",
            bool blocking = true)
        {
            report.Add(new ConfigurationRestoreConflictOut
            {
                Code = code,
                Severity = blocking ? "Critical" : "Warning",
                Section = section,
                EntityId = entityId,
                EntityName = entityName,
                Message = message,
                SuggestedAction = suggestedAction,
                Blocking = blocking
            });
        }

        foreach (var unknownId in requestedDeviceIds.Where(x => !snapshotDeviceIds.Contains(x)))
            AddConflict(
                "TargetNotInSnapshot",
                "Escopo",
                $"O equipamento selecionado {unknownId} não pertence a este snapshot.",
                "Atualize a lista de equipamentos e execute uma nova simulação.",
                unknownId);
        if (requestedDeviceIds.Count > 50)
            AddConflict(
                "TargetLimitExceeded",
                "Escopo",
                "A restauração em massa está limitada a 50 equipamentos por execução.",
                "Divida a operação em lotes menores.");

        if (input.IncludeDevices)
        {
            var current = await context.Devices.AsNoTracking()
                .Where(x => x.LicenseId == backup.LicenseId)
                .Select(x => new { x.Id, x.Name, x.IsActive, x.SerialNumber, x.MACAddress })
                .ToListAsync(cancellationToken);
            var snapshots = payload.Devices.Where(x => targetDeviceIds.Contains(x.Id)).ToList();
            var snapshotIds = snapshots.Select(x => x.Id).ToHashSet();
            var currentIds = current.Select(x => x.Id).ToHashSet();
            var conflictStart = report.Count(x => x.Blocking);
            var foreignIds = await context.Devices.AsNoTracking()
                .Where(x => snapshotIds.Contains(x.Id) && x.LicenseId != backup.LicenseId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in foreignIds)
                AddConflict(
                    "DeviceBelongsToAnotherLicense",
                    "Equipamentos",
                    $"O equipamento {id} pertence a outra licença.",
                    "Remova o equipamento do escopo ou corrija o vínculo de licença.",
                    id);
            var naturalConflicts = snapshots.Where(snapshot =>
                    current.Any(existing =>
                        existing.Id != snapshot.Id &&
                        (!string.IsNullOrWhiteSpace(snapshot.SerialNumber) && existing.SerialNumber == snapshot.SerialNumber) ||
                        existing.Id != snapshot.Id &&
                        (!string.IsNullOrWhiteSpace(snapshot.MacAddress) && existing.MACAddress == snapshot.MacAddress)))
                .Select(x => x.Name)
                .Distinct()
                .ToList();
            foreach (var name in naturalConflicts)
                AddConflict(
                    "DuplicateDeviceIdentity",
                    "Equipamentos",
                    $"Já existe outro equipamento com o serial ou MAC de {name}.",
                    "Revise serial e MAC antes de restaurar.",
                    entityName: name);
            sections.Add(new ConfigurationRestoreSectionOut
            {
                Section = "Equipamentos",
                CreateCount = snapshotIds.Count(x => !currentIds.Contains(x)),
                UpdateCount = snapshotIds.Count(x => currentIds.Contains(x)),
                DeactivateCount = mode == "Reconcile" && isMassRestore
                    ? current.Count(x => x.IsActive && !snapshotIds.Contains(x.Id))
                    : 0,
                ConflictCount = report.Count(x => x.Blocking) - conflictStart
            });
        }

        if (input.IncludeRoutes)
        {
            var routeDeviceSnapshots = isMassRestore
                ? payload.RouteDevices
                : payload.RouteDevices.Where(x => targetDeviceIds.Contains(x.DeviceId)).ToList();
            var selectedRouteIds = isMassRestore
                ? payload.Routes.Select(x => x.Id).ToHashSet()
                : routeDeviceSnapshots.Select(x => x.AccessRouteId).ToHashSet();
            var routeSnapshots = payload.Routes.Where(x => selectedRouteIds.Contains(x.Id)).ToList();
            var current = await context.AccessRoutes.AsNoTracking()
                .Where(x => x.LicenseId == backup.LicenseId && (isMassRestore || selectedRouteIds.Contains(x.Id)))
                .Select(x => new { x.Id, x.IsActive, x.Name })
                .ToListAsync(cancellationToken);
            var snapshotIds = routeSnapshots.Select(x => x.Id).ToHashSet();
            var currentIds = current.Select(x => x.Id).ToHashSet();
            var conflictStart = report.Count(x => x.Blocking);
            var foreignIds = await context.AccessRoutes.AsNoTracking()
                .Where(x => snapshotIds.Contains(x.Id) && x.LicenseId != backup.LicenseId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in foreignIds)
                AddConflict(
                    "RouteBelongsToAnotherLicense",
                    "Rotas e portais",
                    $"A rota {id} pertence a outra licença.",
                    "Corrija o vínculo da rota antes de restaurar.",
                    id);
            var duplicateNames = routeSnapshots.Where(snapshot =>
                    current.Any(existing => existing.Name == snapshot.Name && existing.Id != snapshot.Id))
                .Select(x => x.Name)
                .Distinct()
                .ToList();
            foreach (var name in duplicateNames)
                AddConflict(
                    "DuplicateRouteName",
                    "Rotas e portais",
                    $"Já existe outra rota com o nome {name}.",
                    "Renomeie a rota atual ou retire-a do escopo.",
                    entityName: name);
            var availableDevices = input.IncludeDevices
                ? targetDeviceIds
                : await context.Devices.AsNoTracking()
                    .Where(x => x.LicenseId == backup.LicenseId)
                    .Select(x => x.Id)
                    .ToHashSetAsync(cancellationToken);
            var missingDevices = routeDeviceSnapshots
                .Where(x => !availableDevices.Contains(x.DeviceId))
                .Select(x => x.DeviceId)
                .Distinct()
                .ToList();
            foreach (var id in missingDevices)
                AddConflict(
                    "MissingRouteDevice",
                    "Rotas e portais",
                    $"A rota depende do equipamento ausente {id}.",
                    "Inclua o equipamento na restauração ou restaure apenas a configuração disponível.",
                    id);
            var selectedOverrides = isMassRestore
                ? payload.RouteOverrides
                : [];
            var overrideResidentIds = selectedOverrides.Select(x => x.ResidentId).Distinct().ToHashSet();
            var existingOverrideResidentIds = await context.Residents.AsNoTracking()
                .ForLicense(backup.LicenseId)
                .Where(x => overrideResidentIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSetAsync(cancellationToken);
            var missingOverrideResidents = overrideResidentIds
                .Where(x => !existingOverrideResidentIds.Contains(x))
                .ToList();
            foreach (var id in missingOverrideResidents)
                AddConflict(
                    "MissingRouteResident",
                    "Rotas e portais",
                    $"A exceção de rota depende da pessoa ausente {id}.",
                    "Cadastre novamente a pessoa ou remova a exceção do snapshot.",
                    id);
            sections.Add(new ConfigurationRestoreSectionOut
            {
                Section = "Rotas e portais",
                CreateCount = snapshotIds.Count(x => !currentIds.Contains(x)) + routeDeviceSnapshots.Count + selectedOverrides.Count,
                UpdateCount = snapshotIds.Count(x => currentIds.Contains(x)),
                DeactivateCount = mode == "Reconcile" && isMassRestore
                    ? current.Count(x => x.IsActive && !snapshotIds.Contains(x.Id))
                    : 0,
                ConflictCount = report.Count(x => x.Blocking) - conflictStart
            });
        }

        if (input.IncludeCredentials)
        {
            var bindingSnapshots = isMassRestore
                ? payload.Bindings
                : payload.Bindings.Where(x => targetDeviceIds.Contains(x.DeviceId)).ToList();
            var selectedCredentialIds = isMassRestore
                ? payload.Credentials.Select(x => x.Id).ToHashSet()
                : bindingSnapshots.Select(x => x.ResidentAccessCredentialId).ToHashSet();
            var credentialSnapshots = payload.Credentials
                .Where(x => selectedCredentialIds.Contains(x.Id))
                .ToList();
            var current = await context.ResidentAccessCredentials.AsNoTracking()
                .ForLicense(backup.LicenseId)
                .Where(x => isMassRestore || selectedCredentialIds.Contains(x.Id))
                .Select(x => new { x.Id, x.IsActive })
                .ToListAsync(cancellationToken);
            var snapshotIds = credentialSnapshots.Select(x => x.Id).ToHashSet();
            var currentIds = current.Select(x => x.Id).ToHashSet();
            var conflictStart = report.Count(x => x.Blocking);
            var foreignIds = await context.ResidentAccessCredentials.AsNoTracking()
                .Where(x => snapshotIds.Contains(x.Id))
                .Where(x => !x.Resident.UnitLinks.Any(link => link.Unit.Block.LicenseId == backup.LicenseId) &&
                            (x.Resident.UnitLinks.Any() || x.Resident.Unit.Block.LicenseId != backup.LicenseId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in foreignIds)
                AddConflict(
                    "CredentialBelongsToAnotherLicense",
                    "Credenciais e vínculos",
                    $"A credencial {id} pertence a outra licença.",
                    "Retire a credencial do escopo e revise o vínculo da pessoa.",
                    id);
            var residentIds = credentialSnapshots.Select(x => x.ResidentId).Distinct().ToHashSet();
            var existingResidentIds = await context.Residents.AsNoTracking()
                .ForLicense(backup.LicenseId)
                .Where(x => residentIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSetAsync(cancellationToken);
            var missingResidents = residentIds.Where(x => !existingResidentIds.Contains(x)).ToList();
            foreach (var id in missingResidents)
                AddConflict(
                    "MissingCredentialResident",
                    "Credenciais e vínculos",
                    $"A pessoa vinculada {id} não existe mais nesta licença.",
                    "Restaure ou recadastre a pessoa antes de continuar.",
                    id);

            var availableDevices = input.IncludeDevices
                ? targetDeviceIds
                : await context.Devices.AsNoTracking()
                    .Where(x => x.LicenseId == backup.LicenseId)
                    .Select(x => x.Id)
                    .ToHashSetAsync(cancellationToken);
            var missingBindingDevices = bindingSnapshots
                .Where(x => !availableDevices.Contains(x.DeviceId))
                .Select(x => x.DeviceId)
                .Distinct()
                .ToList();
            foreach (var id in missingBindingDevices)
                AddConflict(
                    "MissingCredentialDevice",
                    "Credenciais e vínculos",
                    $"Uma credencial depende do equipamento ausente {id}.",
                    "Inclua o equipamento no escopo ou remova o vínculo inconsistente.",
                    id);
            sections.Add(new ConfigurationRestoreSectionOut
            {
                Section = "Credenciais e vinculos",
                CreateCount = snapshotIds.Count(x => !currentIds.Contains(x)) + bindingSnapshots.Count,
                UpdateCount = snapshotIds.Count(x => currentIds.Contains(x)) +
                    (!isMassRestore || payload.Policy is null ? 0 : 1) +
                    (!isMassRestore || payload.AlertNotifications is null ? 0 : 1),
                DeactivateCount = mode == "Reconcile" && isMassRestore
                    ? current.Count(x => x.IsActive && !snapshotIds.Contains(x.Id))
                    : 0,
                ConflictCount = report.Count(x => x.Blocking) - conflictStart
            });
        }

        var equipmentComparisons = input.CompareWithEquipment && targetDeviceIds.Count > 0
            ? await CompareWithEquipmentAsync(backup.LicenseId, payload, targetDeviceIds, report, cancellationToken)
            : [];
        var blockingConflicts = report.Where(x => x.Blocking).ToList();
        var hasSelectedScope = requestedDeviceIds.Count == 0 || targetDeviceIds.Count > 0;

        return new ConfigurationRestorePreviewOut
        {
            BackupId = backup.Id,
            Version = backup.Version,
            Mode = mode,
            CanRestore = blockingConflicts.Count == 0 && hasSelectedScope &&
                (input.IncludeDevices || input.IncludeRoutes || input.IncludeCredentials),
            CreateCount = sections.Sum(x => x.CreateCount),
            UpdateCount = sections.Sum(x => x.UpdateCount),
            DeactivateCount = sections.Sum(x => x.DeactivateCount),
            ConflictCount = blockingConflicts.Count,
            WarningCount = report.Count(x => !x.Blocking),
            SelectedDeviceCount = targetDeviceIds.Count,
            IsMassRestore = isMassRestore,
            ComparedAt = DateTime.UtcNow,
            Sections = sections,
            Conflicts = blockingConflicts.Select(x => x.Message).Take(50).ToList(),
            ConflictReport = report.Take(200).ToList(),
            EquipmentComparisons = equipmentComparisons
        };
    }

    private async Task<List<ConfigurationEquipmentComparisonOut>> CompareWithEquipmentAsync(
        Guid licenseId,
        BackupPayload payload,
        HashSet<Guid> targetDeviceIds,
        List<ConfigurationRestoreConflictOut> report,
        CancellationToken cancellationToken)
    {
        var currentDevices = await context.Devices.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && targetDeviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var credentials = payload.Credentials.ToDictionary(x => x.Id);
        var comparisons = new List<ConfigurationEquipmentComparisonOut>();

        foreach (var snapshot in payload.Devices.Where(x => targetDeviceIds.Contains(x.Id)).OrderBy(x => x.Name))
        {
            if (!currentDevices.TryGetValue(snapshot.Id, out var device))
            {
                comparisons.Add(UnavailableComparison(snapshot, "O equipamento ainda não existe na F&F Access atual."));
                AddEquipmentWarning(
                    report,
                    "EquipmentUnavailable",
                    snapshot,
                    "Não foi possível comparar porque o equipamento ainda não existe na F&F Access.",
                    "Restaure o cadastro do equipamento e execute uma nova comparação.");
                continue;
            }

            DeviceCredentialInventoryResult remote;
            try
            {
                remote = await accessControl
                    .ReadCredentialInventoryAsync(mapper.Map<AccessControlDevice>(device))
                    .WaitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                remote = DeviceCredentialInventoryResult.Unavailable(exception.Message);
            }

            if (!remote.Success)
            {
                comparisons.Add(UnavailableComparison(snapshot, Short(remote.Message, 300)));
                AddEquipmentWarning(
                    report,
                    "EquipmentUnavailable",
                    snapshot,
                    $"O equipamento não respondeu à comparação: {Short(remote.Message, 180)}",
                    "Verifique rede, energia e credenciais do terminal; depois repita a simulação.");
                continue;
            }

            var bindings = payload.Bindings.Where(x => x.DeviceId == snapshot.Id).ToList();
            var matched = new HashSet<Guid>();
            var synced = 0;
            var divergent = 0;
            var orphan = 0;
            foreach (var remoteItem in remote.Items)
            {
                var binding = MatchSnapshotBinding(bindings, credentials, remoteItem, matched);
                if (binding is null)
                {
                    orphan++;
                    continue;
                }
                matched.Add(binding.Id);
                var expectedActive = credentials.TryGetValue(binding.ResidentAccessCredentialId, out var credential) && credential.IsActive;
                if (expectedActive && remoteItem.Active) synced++;
                else divergent++;
            }
            var expectedActiveCount = bindings.Count(x =>
                credentials.TryGetValue(x.ResidentAccessCredentialId, out var credential) && credential.IsActive);
            var missing = bindings.Count(x =>
                credentials.TryGetValue(x.ResidentAccessCredentialId, out var credential) &&
                credential.IsActive &&
                !matched.Contains(x.Id));
            var status = divergent + missing + orphan == 0 ? "Synced" : "Divergent";
            comparisons.Add(new ConfigurationEquipmentComparisonOut
            {
                DeviceId = snapshot.Id,
                DeviceName = snapshot.Name,
                Model = snapshot.Model,
                Status = status,
                Message = Short(remote.Message, 300),
                ExpectedCount = expectedActiveCount,
                RemoteCount = remote.Items.Count,
                SyncedCount = synced,
                DivergentCount = divergent,
                MissingCount = missing,
                OrphanCount = orphan
            });
            if (status == "Divergent")
            {
                AddEquipmentWarning(
                    report,
                    "EquipmentDrift",
                    snapshot,
                    $"F&F Access e equipamento divergem: {divergent} diferente(s), {missing} ausente(s) e {orphan} órfão(s).",
                    "Revise o relatório e restaure para enfileirar a reconciliação deste terminal.");
            }
        }
        return comparisons;
    }

    private static CredentialBindingSnapshot? MatchSnapshotBinding(
        IEnumerable<CredentialBindingSnapshot> bindings,
        IReadOnlyDictionary<Guid, CredentialSnapshot> credentials,
        DeviceCredentialInventoryItem remote,
        HashSet<Guid> matched)
    {
        var available = bindings.Where(x => !matched.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(remote.ExternalCredentialId))
        {
            var match = available.FirstOrDefault(x => string.Equals(
                x.ExternalCredentialId,
                remote.ExternalCredentialId,
                StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        if (!string.IsNullOrWhiteSpace(remote.ExternalUserId))
        {
            var candidates = available.Where(x => string.Equals(
                x.ExternalUserId,
                remote.ExternalUserId,
                StringComparison.OrdinalIgnoreCase));
            var match = remote.Type.HasValue
                ? candidates.FirstOrDefault(x =>
                    credentials.TryGetValue(x.ResidentAccessCredentialId, out var credential) &&
                    credential.CredentialType == remote.Type)
                : candidates.FirstOrDefault();
            if (match is not null) return match;
        }
        return string.IsNullOrWhiteSpace(remote.Identifier)
            ? null
            : available.FirstOrDefault(x =>
                credentials.TryGetValue(x.ResidentAccessCredentialId, out var credential) &&
                string.Equals(credential.Identifier, remote.Identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static ConfigurationEquipmentComparisonOut UnavailableComparison(
        DeviceSnapshot snapshot,
        string message) => new()
    {
        DeviceId = snapshot.Id,
        DeviceName = snapshot.Name,
        Model = snapshot.Model,
        Status = "Unavailable",
        Message = message
    };

    private static void AddEquipmentWarning(
        ICollection<ConfigurationRestoreConflictOut> report,
        string code,
        DeviceSnapshot snapshot,
        string message,
        string action) => report.Add(new ConfigurationRestoreConflictOut
    {
        Code = code,
        Severity = "Warning",
        Section = "Comparação com equipamento",
        EntityId = snapshot.Id,
        EntityName = snapshot.Name,
        Message = message,
        SuggestedAction = action,
        Blocking = false
    });

    private async Task RestoreDevicesAsync(
        Guid licenseId,
        BackupPayload payload,
        HashSet<Guid> targetDeviceIds,
        bool isMassRestore,
        string mode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var snapshots = payload.Devices.Where(x => targetDeviceIds.Contains(x.Id)).ToList();
        var snapshotIds = snapshots.Select(x => x.Id).ToHashSet();
        var current = await context.Devices
            .Where(x => x.LicenseId == licenseId && (isMassRestore || snapshotIds.Contains(x.Id)))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            if (!current.TryGetValue(snapshot.Id, out var device))
            {
                device = new AccessControlDeviceDTO
                {
                    Id = snapshot.Id,
                    LicenseId = licenseId,
                    CreatedAt = snapshot.CreatedAt,
                    Location = new LocationDTO()
                };
                context.Devices.Add(device);
            }
            snapshot.Apply(device, now);
        }
        if (mode == "Reconcile" && isMassRestore)
        {
            foreach (var device in current.Values.Where(x => !snapshotIds.Contains(x.Id)))
            {
                device.IsActive = false;
                device.LastUpdatedAt = now;
                device.HealthMessage = "Desativado por restauracao reconciliada.";
            }
        }
    }

    private async Task RestoreRoutesAsync(
        Guid licenseId,
        BackupPayload payload,
        HashSet<Guid> targetDeviceIds,
        bool isMassRestore,
        string mode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var routeDevices = isMassRestore
            ? payload.RouteDevices
            : payload.RouteDevices.Where(x => targetDeviceIds.Contains(x.DeviceId)).ToList();
        var snapshotIds = isMassRestore
            ? payload.Routes.Select(x => x.Id).ToHashSet()
            : routeDevices.Select(x => x.AccessRouteId).ToHashSet();
        var snapshots = payload.Routes.Where(x => snapshotIds.Contains(x.Id)).ToList();
        var current = await context.AccessRoutes
            .Where(x => x.LicenseId == licenseId && (isMassRestore || snapshotIds.Contains(x.Id)))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            if (!current.TryGetValue(snapshot.Id, out var route))
            {
                route = new AccessRouteDTO
                {
                    Id = snapshot.Id,
                    LicenseId = licenseId,
                    CreatedAt = snapshot.CreatedAt
                };
                context.AccessRoutes.Add(route);
            }
            snapshot.Apply(route, now);
        }
        if (mode == "Reconcile" && isMassRestore)
        {
            foreach (var route in current.Values.Where(x => !snapshotIds.Contains(x.Id)))
            {
                route.IsActive = false;
                route.UpdatedAt = now;
            }
        }

        if (isMassRestore)
        {
            await context.AccessRouteDevices
                .Where(x => snapshotIds.Contains(x.AccessRouteId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.AccessRouteResidentOverrides
                .Where(x => snapshotIds.Contains(x.AccessRouteId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await context.AccessRouteDevices
                .Where(x => snapshotIds.Contains(x.AccessRouteId) && targetDeviceIds.Contains(x.DeviceId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var item in routeDevices)
            context.AccessRouteDevices.Add(item.ToDto());
        if (isMassRestore)
        {
            foreach (var item in payload.RouteOverrides)
                context.AccessRouteResidentOverrides.Add(item.ToDto());
        }
    }

    private async Task RestoreCredentialsAsync(
        Guid licenseId,
        BackupPayload payload,
        HashSet<Guid> targetDeviceIds,
        bool isMassRestore,
        string mode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var bindings = isMassRestore
            ? payload.Bindings
            : payload.Bindings.Where(x => targetDeviceIds.Contains(x.DeviceId)).ToList();
        var snapshotIds = isMassRestore
            ? payload.Credentials.Select(x => x.Id).ToHashSet()
            : bindings.Select(x => x.ResidentAccessCredentialId).ToHashSet();
        var snapshots = payload.Credentials.Where(x => snapshotIds.Contains(x.Id)).ToList();
        var current = await context.ResidentAccessCredentials
            .ForLicense(licenseId)
            .Where(x => isMassRestore || snapshotIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            if (!current.TryGetValue(snapshot.Id, out var credential))
            {
                credential = new ResidentAccessCredentialDTO
                {
                    Id = snapshot.Id,
                    ResidentId = snapshot.ResidentId,
                    CreatedAt = snapshot.CreatedAt
                };
                context.ResidentAccessCredentials.Add(credential);
            }
            snapshot.Apply(credential, now);
        }
        if (mode == "Reconcile" && isMassRestore)
        {
            foreach (var credential in current.Values.Where(x => !snapshotIds.Contains(x.Id)))
            {
                credential.IsActive = false;
                credential.UpdatedAt = now;
            }
        }

        if (isMassRestore)
        {
            await context.ResidentAccessDevices
                .Where(x => snapshotIds.Contains(x.ResidentAccessCredentialId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await context.ResidentAccessDevices
                .Where(x => snapshotIds.Contains(x.ResidentAccessCredentialId) && targetDeviceIds.Contains(x.DeviceId))
                .ExecuteDeleteAsync(cancellationToken);
        }
        foreach (var item in bindings)
            context.ResidentAccessDevices.Add(item.ToPendingDto(now));

        if (isMassRestore && payload.Policy is not null)
        {
            var policy = await context.LicenseCredentialPolicies
                .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
            if (policy is null)
            {
                policy = new LicenseCredentialPolicyDTO { LicenseId = licenseId };
                context.LicenseCredentialPolicies.Add(policy);
            }
            payload.Policy.Apply(policy, now);
        }

        if (isMassRestore && payload.AlertNotifications is not null)
        {
            var notificationPolicy = await context.AlertNotificationPolicies
                .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
            if (notificationPolicy is null)
            {
                notificationPolicy = new AlertNotificationPolicyDTO { LicenseId = licenseId };
                context.AlertNotificationPolicies.Add(notificationPolicy);
            }
            payload.AlertNotifications.Apply(notificationPolicy, now);
        }
    }

    private async Task<(ConfigurationBackupDTO Backup, BackupPayload Payload)?> LoadPayloadAsync(
        Guid licenseId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var backup = await context.ConfigurationBackups
            .FirstOrDefaultAsync(x => x.Id == backupId && x.LicenseId == licenseId, cancellationToken);
        if (backup is null) return null;
        var payload = ParsePayload(licenseId, backup.PayloadJson, backup.Checksum);
        return (backup, payload);
    }

    private static BackupPayload ParsePayload(Guid licenseId, string payloadJson, string checksum)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(checksum),
                Encoding.ASCII.GetBytes(Checksum(payloadJson))))
            throw new ConfigurationRestoreException("CorruptedBackup", "A integridade criptografica deste backup nao pode ser confirmada.");
        BackupPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BackupPayload>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            throw new ConfigurationRestoreException("CorruptedBackup", "O conteudo deste backup esta corrompido.");
        }
        if (payload is null ||
            payload.FormatVersion != FormatVersion ||
            payload.LicenseId != licenseId ||
            payload.Devices is null ||
            payload.Routes is null ||
            payload.RouteDevices is null ||
            payload.Credentials is null ||
            payload.Bindings is null ||
            payload.RouteOverrides is null)
            throw new ConfigurationRestoreException("UnsupportedBackup", "Este backup pertence a outra licenca ou usa uma versao incompativel.");
        ValidatePayload(payload);
        return payload;
    }

    private static void ValidatePayload(BackupPayload payload)
    {
        var deviceIds = payload.Devices.Select(x => x.Id).ToHashSet();
        var routeIds = payload.Routes.Select(x => x.Id).ToHashSet();
        var credentialIds = payload.Credentials.Select(x => x.Id).ToHashSet();
        var invalid = deviceIds.Count != payload.Devices.Count ||
            routeIds.Count != payload.Routes.Count ||
            credentialIds.Count != payload.Credentials.Count ||
            payload.Devices
                .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
                .GroupBy(x => x.SerialNumber, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1) ||
            payload.Devices
                .Where(x => !string.IsNullOrWhiteSpace(x.MacAddress))
                .GroupBy(x => x.MacAddress, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1) ||
            payload.Routes
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1) ||
            payload.RouteDevices.Any(x => !routeIds.Contains(x.AccessRouteId) || !deviceIds.Contains(x.DeviceId)) ||
            payload.RouteOverrides.Any(x => !routeIds.Contains(x.AccessRouteId)) ||
            payload.Bindings.Any(x => !credentialIds.Contains(x.ResidentAccessCredentialId) || !deviceIds.Contains(x.DeviceId));
        if (invalid)
            throw new ConfigurationRestoreException("CorruptedBackup", "Os vinculos internos deste backup estao inconsistentes.");
    }

    private static string NormalizeMode(string value) =>
        string.Equals(value?.Trim(), "Reconcile", StringComparison.OrdinalIgnoreCase) ? "Reconcile" : "Merge";

    private static string ModeLabel(string value) => value == "Reconcile" ? "reconciliar" : "mesclar";

    private static string NormalizeName(string value, int version) =>
        string.IsNullOrWhiteSpace(value) ? $"Backup v{version}" : Short(value.Trim(), 120);

    private static string Checksum(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record BackupPayload(
        int FormatVersion,
        Guid LicenseId,
        DateTime CapturedAt,
        List<DeviceSnapshot> Devices,
        List<RouteSnapshot> Routes,
        List<RouteDeviceSnapshot> RouteDevices,
        List<CredentialSnapshot> Credentials,
        List<CredentialBindingSnapshot> Bindings,
        List<RouteOverrideSnapshot> RouteOverrides,
        CredentialPolicySnapshot? Policy,
        AlertNotificationPolicySnapshot? AlertNotifications);

    private sealed record DeviceSnapshot(
        Guid Id,
        string Name,
        string IPAddress,
        int Port,
        string Username,
        string Password,
        string? MacAddress,
        string Model,
        string? SerialNumber,
        string? FirmwareVersion,
        DeviceTypeEnum Type,
        bool IsActive,
        float LocationX,
        float LocationY,
        string CapacityJson,
        string DiscoveredPortalsJson,
        DateTime CreatedAt)
    {
        public static DeviceSnapshot From(AccessControlDeviceDTO value) => new(
            value.Id, value.Name, value.IPAddress, value.Port, value.Username, value.Password,
            value.MACAddress, value.Model, value.SerialNumber, value.FirmwareVersion, value.Type,
            value.IsActive, value.Location.X, value.Location.Y, value.CapacityJson,
            value.DiscoveredPortalsJson, value.CreatedAt);

        public void Apply(AccessControlDeviceDTO value, DateTime now)
        {
            value.Name = Name;
            value.IPAddress = IPAddress;
            value.Port = Port;
            value.Username = Username;
            value.Password = Password;
            value.MACAddress = MacAddress;
            value.Model = Model;
            value.SerialNumber = SerialNumber;
            value.FirmwareVersion = FirmwareVersion;
            value.Type = Type;
            value.IsActive = IsActive;
            value.Location ??= new LocationDTO();
            value.Location.X = LocationX;
            value.Location.Y = LocationY;
            value.CapacityJson = CapacityJson;
            value.DiscoveredPortalsJson = DiscoveredPortalsJson;
            value.LastUpdatedAt = now;
            value.HealthMessage = "Configuracao restaurada; aguardando verificacao.";
        }
    }

    private sealed record RouteSnapshot(
        Guid Id,
        string Name,
        string Description,
        AccessRouteAudienceEnum Audience,
        bool IsActive,
        bool AllowTemporary,
        int DaysOfWeekMask,
        TimeSpan StartTime,
        TimeSpan EndTime,
        DateTime CreatedAt)
    {
        public static RouteSnapshot From(AccessRouteDTO value) => new(
            value.Id, value.Name, value.Description, value.Audience, value.IsActive,
            value.AllowTemporary, value.DaysOfWeekMask, value.StartTime, value.EndTime, value.CreatedAt);

        public void Apply(AccessRouteDTO value, DateTime now)
        {
            value.Name = Name;
            value.Description = Description;
            value.Audience = Audience;
            value.IsActive = IsActive;
            value.AllowTemporary = AllowTemporary;
            value.DaysOfWeekMask = DaysOfWeekMask;
            value.StartTime = StartTime;
            value.EndTime = EndTime;
            value.UpdatedAt = now;
        }
    }

    private sealed record RouteDeviceSnapshot(
        Guid Id,
        Guid AccessRouteId,
        Guid DeviceId,
        int PortalNumber,
        AccessRouteDirectionEnum Direction,
        bool IsActive)
    {
        public static RouteDeviceSnapshot From(AccessRouteDeviceDTO value) => new(
            value.Id, value.AccessRouteId, value.DeviceId, value.PortalNumber, value.Direction, value.IsActive);

        public AccessRouteDeviceDTO ToDto() => new()
        {
            Id = Guid.NewGuid(),
            AccessRouteId = AccessRouteId,
            DeviceId = DeviceId,
            PortalNumber = PortalNumber,
            Direction = Direction,
            IsActive = IsActive
        };
    }

    private sealed record CredentialSnapshot(
        Guid Id,
        Guid ResidentId,
        AccessCredentialTypeEnum CredentialType,
        string Identifier,
        bool IsActive,
        bool IsTemporary,
        int RenewalCount,
        int MaxRenewals,
        int UseCount,
        int? MaxUses,
        DateTime ValidFrom,
        DateTime ValidTo,
        DateTime CreatedAt)
    {
        public static CredentialSnapshot From(ResidentAccessCredentialDTO value) => new(
            value.Id, value.ResidentId, value.CredentialType, value.Identifier, value.IsActive,
            value.IsTemporary, value.RenewalCount, value.MaxRenewals, value.UseCount, value.MaxUses,
            value.ValidFrom, value.ValidTo, value.CreatedAt);

        public void Apply(ResidentAccessCredentialDTO value, DateTime now)
        {
            value.ResidentId = ResidentId;
            value.CredentialType = CredentialType;
            value.Identifier = Identifier;
            value.IsActive = IsActive;
            value.IsTemporary = IsTemporary;
            value.RenewalCount = RenewalCount;
            value.MaxRenewals = MaxRenewals;
            value.UseCount = UseCount;
            value.MaxUses = MaxUses;
            value.ValidFrom = ValidFrom;
            value.ValidTo = ValidTo;
            value.UpdatedAt = now;
        }
    }

    private sealed record CredentialBindingSnapshot(
        Guid Id,
        Guid ResidentAccessCredentialId,
        Guid DeviceId,
        DeviceTypeEnum DeviceType,
        string ExternalUserId,
        string ExternalCredentialId,
        string RouteNames,
        string PortalNumbers)
    {
        public static CredentialBindingSnapshot From(ResidentAccessDeviceDTO value) => new(
            value.Id, value.ResidentAccessCredentialId, value.DeviceId, value.DeviceType,
            value.ExternalUserId, value.ExternalCredentialId, value.RouteNames, value.PortalNumbers);

        public ResidentAccessDeviceDTO ToPendingDto(DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            ResidentAccessCredentialId = ResidentAccessCredentialId,
            DeviceId = DeviceId,
            DeviceType = DeviceType,
            ExternalUserId = ExternalUserId,
            ExternalCredentialId = ExternalCredentialId,
            ExtraJson = """{"source":"backup-restore"}""",
            IsSynced = false,
            LastSyncAt = now,
            SyncStatus = CredentialSyncStatusEnum.Pending,
            AttemptCount = 0,
            NextAttemptAt = now,
            RouteNames = RouteNames,
            PortalNumbers = PortalNumbers
        };
    }

    private sealed record RouteOverrideSnapshot(
        Guid Id,
        Guid AccessRouteId,
        Guid ResidentId,
        AccessRouteOverrideModeEnum Mode,
        string Reason,
        DateTime CreatedAt)
    {
        public static RouteOverrideSnapshot From(AccessRouteResidentOverrideDTO value) => new(
            value.Id, value.AccessRouteId, value.ResidentId, value.Mode, value.Reason, value.CreatedAt);

        public AccessRouteResidentOverrideDTO ToDto() => new()
        {
            Id = Guid.NewGuid(),
            AccessRouteId = AccessRouteId,
            ResidentId = ResidentId,
            Mode = Mode,
            Reason = Reason,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private sealed record CredentialPolicySnapshot(
        int QrCodeValidityMinutes,
        bool AllowQrCodeRenewal,
        int MaxQrCodeRenewals,
        int QrCodeRenewalMinutes,
        int TemporaryFaceValidityMinutes,
        int MaxTemporaryFaceValidityMinutes,
        bool RequireFacePhoto,
        bool AutoDeactivateExpiredCredentials,
        bool RemoveExpiredCredentialsFromDevices)
    {
        public static CredentialPolicySnapshot From(LicenseCredentialPolicyDTO value) => new(
            value.QrCodeValidityMinutes, value.AllowQrCodeRenewal, value.MaxQrCodeRenewals,
            value.QrCodeRenewalMinutes, value.TemporaryFaceValidityMinutes,
            value.MaxTemporaryFaceValidityMinutes, value.RequireFacePhoto,
            value.AutoDeactivateExpiredCredentials, value.RemoveExpiredCredentialsFromDevices);

        public void Apply(LicenseCredentialPolicyDTO value, DateTime now)
        {
            value.QrCodeValidityMinutes = QrCodeValidityMinutes;
            value.AllowQrCodeRenewal = AllowQrCodeRenewal;
            value.MaxQrCodeRenewals = MaxQrCodeRenewals;
            value.QrCodeRenewalMinutes = QrCodeRenewalMinutes;
            value.TemporaryFaceValidityMinutes = TemporaryFaceValidityMinutes;
            value.MaxTemporaryFaceValidityMinutes = MaxTemporaryFaceValidityMinutes;
            value.RequireFacePhoto = RequireFacePhoto;
            value.AutoDeactivateExpiredCredentials = AutoDeactivateExpiredCredentials;
            value.RemoveExpiredCredentialsFromDevices = RemoveExpiredCredentialsFromDevices;
            value.UpdatedAt = now;
        }
    }

    private sealed record AlertNotificationPolicySnapshot(
        bool Enabled,
        OperationalAlertSeverity MinimumSeverity,
        int WarningSlaMinutes,
        int CriticalSlaMinutes,
        int EscalationRepeatMinutes,
        bool WebhookEnabled,
        string WebhookUrl,
        string WebhookSecret,
        bool EmailEnabled,
        string EmailRecipients,
        string SmtpHost,
        int SmtpPort,
        string SmtpUsername,
        string SmtpPassword,
        string SmtpFromEmail,
        string SmtpFromName,
        bool SmtpEnableSsl)
    {
        public static AlertNotificationPolicySnapshot From(AlertNotificationPolicyDTO value) => new(
            value.Enabled,
            value.MinimumSeverity,
            value.WarningSlaMinutes,
            value.CriticalSlaMinutes,
            value.EscalationRepeatMinutes,
            value.WebhookEnabled,
            value.WebhookUrl,
            value.WebhookSecret,
            value.EmailEnabled,
            value.EmailRecipients,
            value.SmtpHost,
            value.SmtpPort,
            value.SmtpUsername,
            value.SmtpPassword,
            value.SmtpFromEmail,
            value.SmtpFromName,
            value.SmtpEnableSsl);

        public void Apply(AlertNotificationPolicyDTO value, DateTime now)
        {
            value.Enabled = Enabled;
            value.MinimumSeverity = MinimumSeverity;
            value.WarningSlaMinutes = WarningSlaMinutes;
            value.CriticalSlaMinutes = CriticalSlaMinutes;
            value.EscalationRepeatMinutes = EscalationRepeatMinutes;
            value.WebhookEnabled = WebhookEnabled;
            value.WebhookUrl = WebhookUrl;
            value.WebhookSecret = WebhookSecret;
            value.EmailEnabled = EmailEnabled;
            value.EmailRecipients = EmailRecipients;
            value.SmtpHost = SmtpHost ?? string.Empty;
            value.SmtpPort = SmtpPort is > 0 and <= 65535 ? SmtpPort : 587;
            value.SmtpUsername = SmtpUsername ?? string.Empty;
            value.SmtpPassword = SmtpPassword ?? string.Empty;
            value.SmtpFromEmail = SmtpFromEmail ?? string.Empty;
            value.SmtpFromName = string.IsNullOrWhiteSpace(SmtpFromName) ? "F&F Access" : SmtpFromName;
            value.SmtpEnableSsl = SmtpEnableSsl;
            value.UpdatedAt = now;
        }
    }
}

public sealed class ConfigurationRestoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

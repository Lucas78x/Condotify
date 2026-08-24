using AutoMapper;
using Condotify.Models;
using CondotifyAPI.Data.AccessControl;
using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Security.Cryptography;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/licenses/{licenseId:guid}")]
[Authorize]
[RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
public sealed class CredentialManagementController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControl;
    private readonly IAccessRouteResolver _routeResolver;
    private readonly IMapper _mapper;
    private readonly ILogger<CredentialManagementController> _logger;
    private readonly IPrivateMediaStore _media;

    public CredentialManagementController(
        DatabaseContext context,
        IAccessControlService accessControl,
        IAccessRouteResolver routeResolver,
        IMapper mapper,
        ILogger<CredentialManagementController> logger,
        IPrivateMediaStore media)
    {
        _context = context;
        _accessControl = accessControl;
        _routeResolver = routeResolver;
        _mapper = mapper;
        _logger = logger;
        _media = media;
    }

    [HttpPost("residents/{residentId:guid}/facial/activate-by-routes")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    [RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> ActivateFacialByRoutes(
        Guid licenseId,
        Guid residentId,
        [FromBody] ActivateFacialByRoutesIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (string.IsNullOrWhiteSpace(input.ImageBase64))
            return BadRequest(new { Result = "PhotoRequired", Errors = "Adicione uma foto antes de ativar o reconhecimento facial." });

        var resident = await _context.Residents
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.AccessCredentials).ThenInclude(x => x.Devices)
            .ForLicense(licenseId)
            .FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident is null) return NotFound();

        var credential = resident.AccessCredentials.FirstOrDefault(x =>
            x.CredentialType == AccessCredentialTypeEnum.Face && x.ArchivedAt == null);
        var resolution = await _routeResolver.ResolveAsync(licenseId, resident, AccessCredentialTypeEnum.Face);
        if (resolution.Targets.Count == 0)
        {
            var routeMessage = resolution.RouteNames.Count == 0
                ? "Nenhuma rota ativa atende ao perfil desta pessoa. Configure o publico e os equipamentos em Rotas de acesso."
                : "As rotas deste perfil nao possuem equipamentos faciais ativos. Revise os destinos da rota.";
            if (credential is not null)
            {
                credential.IsActive = false;
                credential.UpdatedAt = DateTime.UtcNow;
                var knownDevices = await DeviceLookupAsync(licenseId);
                var pendingRemoval = new List<string>();
                foreach (var binding in credential.Devices.ToList())
                {
                    if (!knownDevices.TryGetValue(binding.DeviceId, out var device))
                    {
                        _context.ResidentAccessDevices.Remove(binding);
                        continue;
                    }

                    var removal = await ExecuteSafelyAsync(() => _accessControl.RemoveCredentialAsync(
                        _mapper.Map<AccessControlDevice>(device), BuildRequest(credential, resident, binding, null)));
                    AddAudit(device.Id, ActionTypeEnum.RemoveCredential,
                        $"Pessoa sem rota elegivel | {resident.Name} | {(removal.Success ? "Removido" : "Pendente")}: {removal.Message}");
                    if (removal.Success) _context.ResidentAccessDevices.Remove(binding);
                    else
                    {
                        UpdateBinding(binding, removal, CredentialSyncStatusEnum.RemovalPending);
                        pendingRemoval.Add(device.Name);
                    }
                }
                await _context.SaveChangesAsync();
                routeMessage += pendingRemoval.Count == 0
                    ? " A facial foi suspensa e removida dos vinculos anteriores."
                    : $" A facial foi suspensa; remocao pendente em: {string.Join(", ", pendingRemoval)}.";
            }
            return Conflict(new { Result = "NoEligibleRoute", Errors = routeMessage });
        }

        var resolvedImage = await _media.ResolveDataUriAsync(licenseId, input.ImageBase64, HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(resolvedImage))
            return BadRequest(new { Result = "PhotoRequired", Errors = "A foto nao esta mais disponivel. Envie uma nova imagem." });
        var imageLimit = resolution.Targets.Any(x => x.Device.Type.IsInIntelbras()) ? 100_000 : 1_000_000;
        var imageValidation = FaceImageValidator.Validate(resolvedImage, imageLimit);
        if (!imageValidation.Success)
            return BadRequest(new { Result = "InvalidImage", Errors = imageValidation.Error });

        var now = DateTime.UtcNow;
        var policy = await GetPolicyAsync(licenseId);
        if (credential is null)
        {
            var isTemporary = resident.Temporary;
            var validFrom = now;
            credential = new ResidentAccessCredentialDTO
            {
                Id = Guid.NewGuid(),
                ResidentId = resident.Id,
                Resident = resident,
                CredentialType = AccessCredentialTypeEnum.Face,
                Identifier = $"FACE-{resident.Id:N}",
                IsActive = true,
                IsTemporary = isTemporary,
                ValidFrom = validFrom,
                ValidTo = isTemporary
                    ? ResolveValidTo(new CreateCredentialIn { Type = AccessCredentialTypeEnum.Face }, resident, policy, validFrom, true)
                    : validFrom.AddYears(10),
                CreatedAt = now,
                UpdatedAt = now,
                Devices = []
            };
            _context.ResidentAccessCredentials.Add(credential);
            await _context.SaveChangesAsync();
        }
        else
        {
            credential.IsActive = true;
            credential.UpdatedAt = now;
            if (credential.ValidTo <= now)
                credential.ValidTo = credential.IsTemporary
                    ? now.AddMinutes(policy.TemporaryFaceValidityMinutes)
                    : now.AddYears(10);
        }

        var devices = await DeviceLookupAsync(licenseId);
        var targetIds = resolution.Targets.Select(x => x.Device.Id).ToHashSet();
        var removedCount = 0;
        var failedRemovalDevices = new List<string>();
        foreach (var staleBinding in credential.Devices.Where(x => !targetIds.Contains(x.DeviceId)).ToList())
        {
            if (!devices.TryGetValue(staleBinding.DeviceId, out var staleDevice))
            {
                _context.ResidentAccessDevices.Remove(staleBinding);
                removedCount++;
                continue;
            }

            var removal = await ExecuteSafelyAsync(() => _accessControl.RemoveCredentialAsync(
                _mapper.Map<AccessControlDevice>(staleDevice), BuildRequest(credential, resident, staleBinding, null)));
            AddAudit(staleDevice.Id, ActionTypeEnum.RemoveCredential,
                $"Reconciliacao de rota | {resident.Name} | {(removal.Success ? "Removido" : "Pendente")}: {removal.Message}");
            if (removal.Success)
            {
                _context.ResidentAccessDevices.Remove(staleBinding);
                removedCount++;
            }
            else
            {
                UpdateBinding(staleBinding, removal, CredentialSyncStatusEnum.RemovalPending);
                failedRemovalDevices.Add(staleDevice.Name);
            }
        }

        var syncedCount = 0;
        var failedDevices = new List<string>();
        foreach (var target in resolution.Targets)
        {
            var binding = credential.Devices.FirstOrDefault(x => x.DeviceId == target.Device.Id);
            var operation = await ExecuteSafelyAsync(() => _accessControl.UpsertCredentialAsync(
                _mapper.Map<AccessControlDevice>(target.Device),
                BuildRequest(credential, resident, binding, resolvedImage, target.Portals)));

            if (binding is null)
            {
                binding = NewBinding(credential, target.Device, operation, now, target.Portals);
                _context.ResidentAccessDevices.Add(binding);
            }
            else UpdateBinding(binding, operation, portals: target.Portals);

            if (operation.Success) syncedCount++;
            else failedDevices.Add(target.Device.Name);
            AddAudit(target.Device.Id, ActionTypeEnum.ProvisionCredential,
                $"Rotas {string.Join(", ", target.Portals.Select(x => x.RouteName).Distinct())} | {resident.Name} | {(operation.Success ? "Sincronizado" : "Pendente")}: {operation.Message}");
        }

        await _context.SaveChangesAsync();
        var currentBindings = await _context.ResidentAccessDevices.AsNoTracking()
            .Where(x => x.ResidentAccessCredentialId == credential.Id).ToListAsync();
        var output = ToOut(credential, devices, currentBindings);
        var failureText = failedDevices.Count == 0 ? string.Empty : $" Pendentes: {string.Join(", ", failedDevices)}.";
        var removalText = removedCount == 0 ? string.Empty : $" {removedCount} vinculo(s) antigo(s) removido(s).";
        var pendingRemovalText = failedRemovalDevices.Count == 0
            ? string.Empty
            : $" Remocao pendente em: {string.Join(", ", failedRemovalDevices)}.";

        return Ok(new CredentialOperationOut
        {
            Success = true,
            Synced = syncedCount == resolution.Targets.Count && failedDevices.Count == 0 && failedRemovalDevices.Count == 0,
            Message = $"Facial sincronizada em {syncedCount} de {resolution.Targets.Count} equipamento(s) pelas rotas {string.Join(", ", resolution.RouteNames)}.{failureText}{removalText}{pendingRemovalText}",
            Credential = output
        });
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var policy = await GetPolicyAsync(licenseId);
        if (policy.AutoDeactivateExpiredCredentials)
        {
            await CredentialQuery(licenseId).Where(x => x.IsActive && x.ValidTo <= DateTime.UtcNow)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        }

        var credentials = await CredentialQuery(licenseId).AsNoTracking()
            .OrderBy(x => x.Resident.Name)
            .ThenBy(x => x.CredentialType)
            .ToListAsync();
        var devices = await DeviceLookupAsync(licenseId);

        return Ok(credentials.Select(x => ToOut(x, devices)).ToList());
    }

    [HttpPost("credentials")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    [RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> CreateCredential(Guid licenseId, [FromBody] CreateCredentialIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var validation = ValidateInput(input);
        if (validation is not null) return BadRequest(new { Result = "InvalidRequest", Errors = validation });

        var resident = await _context.Residents
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .ForLicense(licenseId)
            .FirstOrDefaultAsync(x => x.Id == input.ResidentId);
        var deviceDto = await _context.Devices.FirstOrDefaultAsync(x => x.Id == input.DeviceId && x.LicenseId == licenseId);
        if (resident is null || deviceDto is null) return NotFound();

        if (input.Type == AccessCredentialTypeEnum.Face && !deviceDto.Type.SupportsFace())
            return BadRequest(new { Result = "UnsupportedCredential", Errors = "O equipamento selecionado nao suporta reconhecimento facial." });
        if (!deviceDto.IsActive)
            return Conflict(new { Result = "InactiveDevice", Errors = "Teste e ative o equipamento antes de sincronizar credenciais." });

        var policy = await GetPolicyAsync(licenseId);
        var identifier = input.Type switch
        {
            AccessCredentialTypeEnum.Face when string.IsNullOrWhiteSpace(input.Identifier) => $"FACE-{resident.Id:N}",
            AccessCredentialTypeEnum.QrCode when string.IsNullOrWhiteSpace(input.Identifier) => await GenerateQrIdentifierAsync(),
            _ => input.Identifier.Trim()
        };
        var duplicate = await _context.ResidentAccessCredentials.AnyAsync(x =>
            x.ResidentId == resident.Id && x.CredentialType == input.Type && x.Identifier == identifier && x.ArchivedAt == null);
        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Este morador ja possui esta credencial cadastrada." });

        if (input.Type == AccessCredentialTypeEnum.Face && !string.IsNullOrWhiteSpace(input.ImageBase64))
        {
            var limit = deviceDto.Type.IsInIntelbras() ? 100_000 : 1_000_000;
            var imageValidation = FaceImageValidator.Validate(input.ImageBase64, limit);
            if (!imageValidation.Success)
                return BadRequest(new { Result = "InvalidImage", Errors = imageValidation.Error });
        }

        var now = DateTime.UtcNow;
        var validFrom = NormalizeUtc(input.ValidFrom ?? now);
        var isTemporary = input.IsTemporary || resident.Temporary || input.Type == AccessCredentialTypeEnum.QrCode;
        var validTo = ResolveValidTo(input, resident, policy, validFrom, isTemporary);
        if (input.Type == AccessCredentialTypeEnum.Face && isTemporary && policy.RequireFacePhoto && string.IsNullOrWhiteSpace(input.ImageBase64))
            return BadRequest(new { Result = "PhotoRequired", Errors = "A politica desta licenca exige foto para faces temporarias." });
        var credential = new ResidentAccessCredentialDTO
        {
            Id = Guid.NewGuid(),
            ResidentId = resident.Id,
            Resident = resident,
            CredentialType = input.Type,
            Identifier = identifier,
            IsActive = true,
            IsTemporary = isTemporary,
            RenewalCount = 0,
            MaxRenewals = input.Type == AccessCredentialTypeEnum.QrCode && policy.AllowQrCodeRenewal
                ? Math.Min(input.MaxRenewals ?? policy.MaxQrCodeRenewals, policy.MaxQrCodeRenewals)
                : 0,
            MaxUses = input.MaxUses is > 0 ? input.MaxUses : null,
            ValidFrom = validFrom,
            ValidTo = validTo,
            CreatedAt = now,
            UpdatedAt = now,
            Devices = []
        };
        _context.ResidentAccessCredentials.Add(credential);
        await _context.SaveChangesAsync();

        var operation = await ExecuteSafelyAsync(() => _accessControl.UpsertCredentialAsync(
            _mapper.Map<AccessControlDevice>(deviceDto), BuildRequest(credential, resident, null, input.ImageBase64)));
        var binding = NewBinding(credential, deviceDto, operation, now);
        _context.ResidentAccessDevices.Add(binding);
        AddAudit(deviceDto.Id, ActionTypeEnum.ProvisionCredential, $"{resident.Name} | {input.Type} | {(operation.Success ? "Sincronizado" : "Pendente")}: {operation.Message}");
        await _context.SaveChangesAsync();

        var output = ToOut(credential, new Dictionary<Guid, AccessControlDeviceDTO> { [deviceDto.Id] = deviceDto }, [binding]);
        return Created("", new CredentialOperationOut
        {
            Success = true,
            Synced = operation.Success,
            Message = operation.Message ?? (operation.Success ? "Credencial sincronizada." : "Credencial salva como pendente."),
            Credential = output
        });
    }

    [HttpPatch("credentials/{credentialId:guid}/status")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> SetStatus(Guid licenseId, Guid credentialId, [FromBody] SetCredentialStatusIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        if (credential is null) return NotFound();

        credential.IsActive = input.IsActive;
        credential.UpdatedAt = DateTime.UtcNow;
        var devices = await DeviceLookupAsync(licenseId);
        var successCount = 0;

        foreach (var binding in credential.Devices)
        {
            if (!devices.TryGetValue(binding.DeviceId, out var deviceDto)) continue;
            var operation = await ExecuteSafelyAsync(() => _accessControl.SetCredentialActiveAsync(
                _mapper.Map<AccessControlDevice>(deviceDto), BuildRequest(credential, credential.Resident, binding, null), input.IsActive));
            UpdateBinding(binding, operation);
            if (operation.Success) successCount++;
            AddAudit(deviceDto.Id, input.IsActive ? ActionTypeEnum.ActivateCredential : ActionTypeEnum.DeactivateCredential,
                $"{credential.Resident.Name} | {(operation.Success ? "Sucesso" : "Falha")}: {operation.Message}");
        }

        await _context.SaveChangesAsync();
        return Ok(new CredentialOperationOut
        {
            Success = true,
            Synced = credential.Devices.Count > 0 && successCount == credential.Devices.Count,
            Message = credential.Devices.Count == 0
                ? "Status alterado na F&F Access. A credencial ainda não está vinculada a equipamentos."
                : $"Status alterado em {successCount} de {credential.Devices.Count} equipamento(s)."
        });
    }

    [HttpPost("credentials/{credentialId:guid}/restore")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    [RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> Restore(Guid licenseId, Guid credentialId, [FromBody] RestoreCredentialIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        var deviceDto = await _context.Devices.FirstOrDefaultAsync(x => x.Id == input.DeviceId && x.LicenseId == licenseId);
        if (credential is null || deviceDto is null) return NotFound();
        if (credential.CredentialType == AccessCredentialTypeEnum.Face && !deviceDto.Type.SupportsFace())
            return BadRequest(new { Result = "UnsupportedCredential", Errors = "O equipamento selecionado nao suporta reconhecimento facial." });
        if (!deviceDto.IsActive) return Conflict(new { Result = "InactiveDevice", Errors = "O equipamento esta inativo." });

        var binding = credential.Devices.FirstOrDefault(x => x.DeviceId == deviceDto.Id);
        if (credential.CredentialType == AccessCredentialTypeEnum.Face && !string.IsNullOrWhiteSpace(input.ImageBase64))
        {
            var limit = deviceDto.Type.IsInIntelbras() ? 100_000 : 1_000_000;
            var imageValidation = FaceImageValidator.Validate(input.ImageBase64, limit);
            if (!imageValidation.Success)
                return BadRequest(new { Result = "InvalidImage", Errors = imageValidation.Error });
        }
        var operation = await ExecuteSafelyAsync(() => _accessControl.UpsertCredentialAsync(
            _mapper.Map<AccessControlDevice>(deviceDto), BuildRequest(credential, credential.Resident, binding, input.ImageBase64)));

        if (binding is null)
        {
            binding = NewBinding(credential, deviceDto, operation, DateTime.UtcNow);
            _context.ResidentAccessDevices.Add(binding);
        }
        else UpdateBinding(binding, operation);

        AddAudit(deviceDto.Id, ActionTypeEnum.RestoreCredential, $"{credential.Resident.Name} | {(operation.Success ? "Sucesso" : "Falha")}: {operation.Message}");
        await _context.SaveChangesAsync();

        return Ok(new CredentialOperationOut { Success = true, Synced = operation.Success, Message = operation.Message ?? "Restauracao concluida." });
    }

    [HttpPost("credentials/{credentialId:guid}/face-enrollment")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> StartFaceEnrollment(Guid licenseId, Guid credentialId, [FromBody] RestoreCredentialIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        var deviceDto = await _context.Devices.FirstOrDefaultAsync(x => x.Id == input.DeviceId && x.LicenseId == licenseId);
        var binding = credential?.Devices.FirstOrDefault(x => x.DeviceId == input.DeviceId);
        if (credential is null || deviceDto is null || binding is null) return NotFound();
        if (credential.CredentialType != AccessCredentialTypeEnum.Face)
            return BadRequest(new { Result = "InvalidCredential", Errors = "A captura remota esta disponivel apenas para credenciais faciais." });
        if (!deviceDto.Type.SupportsFace())
            return BadRequest(new { Result = "UnsupportedCredential", Errors = "O equipamento selecionado nao suporta reconhecimento facial." });

        var operation = await ExecuteSafelyAsync(() => _accessControl.StartFaceEnrollmentAsync(
            _mapper.Map<AccessControlDevice>(deviceDto), binding.ExternalUserId));
        UpdateBinding(binding, operation);
        AddAudit(deviceDto.Id, ActionTypeEnum.FaceEnrollment, $"{credential.Resident.Name} | {(operation.Success ? "Iniciado" : "Falha")}: {operation.Message}");
        await _context.SaveChangesAsync();

        return operation.Success
            ? Ok(new CredentialOperationOut { Success = true, Synced = true, Message = operation.Message! })
            : StatusCode(StatusCodes.Status422UnprocessableEntity, new { Result = "EnrollmentFailed", Errors = operation.Message });
    }

    [HttpPost("devices/{deviceId:guid}/face-enrollment/cancel")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> CancelFaceEnrollment(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var deviceDto = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (deviceDto is null) return NotFound();
        var operation = await ExecuteSafelyAsync(() => _accessControl.CancelFaceEnrollmentAsync(_mapper.Map<AccessControlDevice>(deviceDto)));
        AddAudit(deviceDto.Id, ActionTypeEnum.FaceEnrollment, $"Cancelamento | {(operation.Success ? "Sucesso" : "Falha")}: {operation.Message}");
        await _context.SaveChangesAsync();
        return operation.Success ? Ok(new { operation.Message }) : UnprocessableEntity(new { Result = "CancelFailed", Errors = operation.Message });
    }

    [HttpDelete("credentials/{credentialId:guid}/devices/{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> RemoveFromDevice(Guid licenseId, Guid credentialId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        var deviceDto = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        var binding = credential?.Devices.FirstOrDefault(x => x.DeviceId == deviceId);
        if (credential is null || deviceDto is null || binding is null) return NotFound();

        var operation = await ExecuteSafelyAsync(() => _accessControl.RemoveCredentialAsync(
            _mapper.Map<AccessControlDevice>(deviceDto), BuildRequest(credential, credential.Resident, binding, null)));
        if (operation.Success) _context.ResidentAccessDevices.Remove(binding);
        else UpdateBinding(binding, operation);
        AddAudit(deviceDto.Id, ActionTypeEnum.RemoveCredential, $"{credential.Resident.Name} | {(operation.Success ? "Sucesso" : "Falha")}: {operation.Message}");
        await _context.SaveChangesAsync();

        return operation.Success ? NoContent() : UnprocessableEntity(new { Result = "RemoveFailed", Errors = operation.Message });
    }

    [HttpDelete("credentials/{credentialId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> DeleteCredential(Guid licenseId, Guid credentialId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        if (credential is null) return NotFound();

        var devices = await DeviceLookupAsync(licenseId);
        var failures = new List<string>();
        foreach (var binding in credential.Devices.ToList())
        {
            if (!devices.TryGetValue(binding.DeviceId, out var deviceDto))
            {
                _context.ResidentAccessDevices.Remove(binding);
                continue;
            }

            var operation = await ExecuteSafelyAsync(() => _accessControl.RemoveCredentialAsync(
                _mapper.Map<AccessControlDevice>(deviceDto), BuildRequest(credential, credential.Resident, binding, null)));
            AddAudit(deviceDto.Id, ActionTypeEnum.RemoveCredential,
                $"Exclusao definitiva | {credential.Resident.Name} | {(operation.Success ? "Sucesso" : "Falha")}: {operation.Message}");

            if (operation.Success)
                _context.ResidentAccessDevices.Remove(binding);
            else
            {
                UpdateBinding(binding, operation);
                failures.Add(deviceDto.Name);
            }
        }

        if (failures.Count > 0)
        {
            credential.IsActive = false;
            credential.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return UnprocessableEntity(new
            {
                Result = "CredentialRemovalPending",
                Errors = $"A credencial foi suspensa, mas nao foi removida de: {string.Join(", ", failures)}. Verifique os equipamentos e tente excluir novamente."
            });
        }

        var isVisitCredential = await _context.AccessVisits.AsNoTracking()
            .AnyAsync(x => x.CredentialId == credential.Id);
        if (isVisitCredential)
        {
            credential.IsActive = false;
            credential.ArchivedAt = DateTime.UtcNow;
            credential.UpdatedAt = credential.ArchivedAt;
        }
        else
        {
            _context.ResidentAccessCredentials.Remove(credential);
        }
        await _context.SaveChangesAsync();
        return Ok(new CredentialOperationOut
        {
            Success = true,
            Synced = true,
            Message = isVisitCredential
                ? "Credencial removida da F&F Access. O historico da visita foi preservado para auditoria."
                : "Credencial removida da F&F Access e dos equipamentos vinculados."
        });
    }

    [HttpPost("credentials/{credentialId:guid}/renew")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> RenewQrCode(Guid licenseId, Guid credentialId)
    {
        var credential = await CredentialQuery(licenseId).FirstOrDefaultAsync(x => x.Id == credentialId);
        if (credential is null) return NotFound();
        if (credential.CredentialType != AccessCredentialTypeEnum.QrCode)
            return BadRequest(new { Result = "InvalidCredential", Errors = "Somente credenciais QR Code podem ser renovadas por este fluxo." });

        var policy = await GetPolicyAsync(licenseId);
        if (!policy.AllowQrCodeRenewal || credential.MaxRenewals == 0)
            return Conflict(new { Result = "RenewalDisabled", Errors = "Esta credencial foi emitida sem renovacoes." });
        if (credential.RenewalCount >= credential.MaxRenewals)
            return Conflict(new { Result = "RenewalLimit", Errors = "O limite de renovacoes desta credencial foi atingido." });

        credential.RenewalCount++;
        credential.ValidTo = (credential.ValidTo > DateTime.UtcNow ? credential.ValidTo : DateTime.UtcNow)
            .AddMinutes(policy.QrCodeRenewalMinutes);
        credential.IsActive = true;
        credential.UpdatedAt = DateTime.UtcNow;
        var devices = await DeviceLookupAsync(licenseId);
        var successCount = 0;
        foreach (var binding in credential.Devices)
        {
            if (!devices.TryGetValue(binding.DeviceId, out var device)) continue;
            var operation = await ExecuteSafelyAsync(() => _accessControl.UpsertCredentialAsync(
                _mapper.Map<AccessControlDevice>(device), BuildRequest(credential, credential.Resident, binding, null)));
            UpdateBinding(binding, operation);
            if (operation.Success) successCount++;
            AddAudit(device.Id, ActionTypeEnum.RenewCredential, $"Renovacao QR | {credential.Resident.Name} | {credential.RenewalCount}/{credential.MaxRenewals}: {operation.Message}");
        }
        await _context.SaveChangesAsync();
        return Ok(new CredentialOperationOut
        {
            Success = true,
            Synced = credential.Devices.Count > 0 && successCount == credential.Devices.Count,
            Message = $"QR Code renovado ate {credential.ValidTo.ToCondotifyTime():dd/MM/yyyy HH:mm}. Renovacao {credential.RenewalCount} de {credential.MaxRenewals}.",
            Credential = ToOut(credential, devices)
        });
    }

    [HttpGet("devices/{deviceId:guid}/access-events")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> GetAccessEvents(Guid licenseId, Guid deviceId, [FromQuery] int take = 50)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var deviceDto = await _context.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (deviceDto is null) return NotFound();

        IReadOnlyList<DeviceAccessEvent> events;
        try
        {
            events = await _accessControl.GetAccessEventsAsync(_mapper.Map<AccessControlDevice>(deviceDto), Math.Clamp(take, 1, 200));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao consultar eventos do equipamento {DeviceId}", deviceId);
            var history = await _context.AccessEventRecords.AsNoTracking()
                .Where(x => x.DeviceId == deviceId)
                .OrderByDescending(x => x.OccurredAt)
                .Take(Math.Clamp(take, 1, 200))
                .Select(x => new AccessEventOut
                {
                    Id = x.ExternalEventId, DeviceId = x.DeviceId, DeviceName = deviceDto.Name,
                    Event = x.Event, Authorized = x.Authorized, OccurredAt = x.OccurredAt,
                    ExternalUserId = x.ExternalUserId, PersonName = x.PersonName, Credential = x.Credential,
                    Portal = x.Portal, Details = x.Details
                }).ToListAsync();
            if (history.Count > 0) return Ok(history);
            return StatusCode(StatusCodes.Status502BadGateway, new { Result = "ReadLogsFailed", Errors = "O equipamento esta offline e ainda nao possui eventos armazenados." });
        }

        AddAudit(deviceDto.Id, ActionTypeEnum.ReadAccessLogs, $"{events.Count} evento(s) consultado(s)");
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Os eventos foram consultados, mas a auditoria nao foi salva para o equipamento {DeviceId}", deviceId);
            _context.ChangeTracker.Clear();
        }

        return Ok(events.Select(x => new AccessEventOut
        {
            Id = x.ExternalId,
            DeviceId = deviceDto.Id,
            DeviceName = deviceDto.Name,
            Event = x.Event,
            Authorized = x.Authorized,
            OccurredAt = x.OccurredAt,
            ExternalUserId = x.ExternalUserId ?? string.Empty,
            PersonName = x.PersonName ?? string.Empty,
            Credential = x.Credential ?? string.Empty,
            Portal = x.Portal ?? string.Empty,
            Details = x.Details ?? string.Empty
        }));
    }

    private IQueryable<ResidentAccessCredentialDTO> CredentialQuery(Guid licenseId) =>
        _context.ResidentAccessCredentials
            .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident).ThenInclude(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Devices)
            .Where(x => x.ArchivedAt == null)
            .ForLicense(licenseId);

    private async Task<Dictionary<Guid, AccessControlDeviceDTO>> DeviceLookupAsync(Guid licenseId) =>
        await _context.Devices.AsNoTracking().Where(x => x.LicenseId == licenseId).ToDictionaryAsync(x => x.Id);

    private static CredentialProvisionRequest BuildRequest(
        ResidentAccessCredentialDTO credential,
        ResidentAccessDTO resident,
        ResidentAccessDeviceDTO? binding,
        string? imageBase64,
        IReadOnlyList<AccessPortalAssignment>? portals = null) =>
        new(
            credential.Id,
            resident.Name,
            AccessControlDeviceRegistration.FromResidentId(resident.Id),
            credential.CredentialType,
            credential.Identifier,
            imageBase64,
            credential.ValidFrom,
            credential.ValidTo,
            credential.IsActive,
            binding?.ExternalUserId,
            binding?.ExternalCredentialId,
            portals);

    private static ResidentAccessDeviceDTO NewBinding(
        ResidentAccessCredentialDTO credential,
        AccessControlDeviceDTO device,
        CredentialOperationResult operation,
        DateTime now,
        IReadOnlyList<AccessPortalAssignment>? portals = null) => new()
        {
            Id = Guid.NewGuid(),
            ResidentAccessCredentialId = credential.Id,
            Credential = credential,
            DeviceId = device.Id,
            DeviceType = device.Type,
            ExternalUserId = operation.ExternalUserId ?? string.Empty,
            ExternalCredentialId = operation.ExternalCredentialId ?? string.Empty,
            ExtraJson = OperationJson(operation),
            IsSynced = operation.Success,
            LastSyncAt = now,
            SyncStatus = operation.Success ? CredentialSyncStatusEnum.Synced : CredentialSyncStatusEnum.Failed,
            AttemptCount = 1,
            LastSuccessAt = operation.Success ? now : null,
            LastErrorAt = operation.Success ? null : now,
            NextAttemptAt = operation.Success ? null : now.AddMinutes(2),
            RouteNames = portals is null ? string.Empty : string.Join(", ", portals.Select(x => x.RouteName).Distinct()),
            PortalNumbers = portals is null ? string.Empty : string.Join(",", portals.Select(x => x.PortalNumber).Distinct().OrderBy(x => x))
        };

    private static void UpdateBinding(
        ResidentAccessDeviceDTO binding,
        CredentialOperationResult operation,
        CredentialSyncStatusEnum? failureStatus = null,
        IReadOnlyList<AccessPortalAssignment>? portals = null)
    {
        binding.ExternalUserId = operation.ExternalUserId ?? binding.ExternalUserId;
        binding.ExternalCredentialId = operation.ExternalCredentialId ?? binding.ExternalCredentialId;
        binding.ExtraJson = OperationJson(operation);
        binding.IsSynced = operation.Success;
        var now = DateTime.UtcNow;
        binding.LastSyncAt = now;
        binding.AttemptCount++;
        binding.SyncStatus = operation.Success ? CredentialSyncStatusEnum.Synced : failureStatus ?? CredentialSyncStatusEnum.Failed;
        binding.LastSuccessAt = operation.Success ? now : binding.LastSuccessAt;
        binding.LastErrorAt = operation.Success ? null : now;
        binding.NextAttemptAt = operation.Success ? null : now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, binding.AttemptCount))));
        if (portals is not null)
        {
            binding.RouteNames = string.Join(", ", portals.Select(x => x.RouteName).Distinct());
            binding.PortalNumbers = string.Join(",", portals.Select(x => x.PortalNumber).Distinct().OrderBy(x => x));
        }
    }

    private static CredentialOut ToOut(
        ResidentAccessCredentialDTO credential,
        IReadOnlyDictionary<Guid, AccessControlDeviceDTO> devices,
        IEnumerable<ResidentAccessDeviceDTO>? bindings = null) => new()
        {
            Id = credential.Id,
            ResidentId = credential.ResidentId,
            ResidentName = credential.Resident.Name,
            UnitNumber = credential.Resident.Unit?.Number ?? credential.Resident.ApartmentNumber,
            Type = credential.CredentialType.ToString(),
            Identifier = credential.CredentialType == AccessCredentialTypeEnum.Password ? "********" : credential.Identifier,
            IsActive = credential.IsActive,
            IsTemporary = credential.IsTemporary,
            RenewalCount = credential.RenewalCount,
            MaxRenewals = credential.MaxRenewals,
            UseCount = credential.UseCount,
            MaxUses = credential.MaxUses,
            CanRenew = credential.CredentialType == AccessCredentialTypeEnum.QrCode && credential.IsActive && credential.RenewalCount < credential.MaxRenewals,
            ValidFrom = credential.ValidFrom,
            ValidTo = credential.ValidTo,
            Devices = (bindings ?? credential.Devices).Select(binding =>
            {
                devices.TryGetValue(binding.DeviceId, out var device);
                return new CredentialDeviceOut
                {
                    DeviceId = binding.DeviceId,
                    DeviceName = device?.Name ?? "Equipamento removido",
                    DeviceType = binding.DeviceType.ToString(),
                    ExternalUserId = binding.ExternalUserId,
                    ExternalCredentialId = binding.ExternalCredentialId,
                    IsSynced = binding.IsSynced,
                    LastSyncAt = binding.LastSyncAt,
                    Message = OperationMessage(binding.ExtraJson),
                    Status = binding.SyncStatus.ToString(),
                    AttemptCount = binding.AttemptCount,
                    NextAttemptAt = binding.NextAttemptAt,
                    LastSuccessAt = binding.LastSuccessAt,
                    RouteNames = binding.RouteNames,
                    PortalNumbers = binding.PortalNumbers
                };
            }).ToList()
        };

    private static string? ValidateInput(CreateCredentialIn input)
    {
        if (input.ResidentId == Guid.Empty || input.DeviceId == Guid.Empty) return "Morador e equipamento sao obrigatorios.";
        if (!Enum.IsDefined(input.Type)) return "Tipo de credencial invalido.";
        if (input.Type is not (AccessCredentialTypeEnum.Face or AccessCredentialTypeEnum.QrCode) && string.IsNullOrWhiteSpace(input.Identifier)) return "Informe o numero ou identificador da credencial.";
        if (input.ValidFrom.HasValue && input.ValidTo.HasValue && input.ValidTo <= input.ValidFrom) return "A validade final deve ser posterior ao inicio.";
        return null;
    }

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await _context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }

    private async Task<CredentialOperationResult> ExecuteSafelyAsync(Func<Task<CredentialOperationResult>> operation)
    {
        try { return await operation(); }
        catch (NotSupportedException ex) { return CredentialOperationResult.Fail(ex.Message); }
        catch (ArgumentException ex) { return CredentialOperationResult.Fail(ex.Message); }
        catch { return CredentialOperationResult.Fail("Falha de comunicacao com o equipamento. A operacao ficou pendente."); }
    }

    private void AddAudit(Guid deviceId, ActionTypeEnum action, string details)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        _context.DeviceAudits.Add(new DeviceAuditDTO
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Action = action,
            ChangedFields = details.Length <= 500 ? details : details[..500],
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal"
        });
    }

    private static string OperationJson(CredentialOperationResult result) => JsonSerializer.Serialize(new { result.Success, result.Message });

    private static string OperationMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("Message", out var message) || document.RootElement.TryGetProperty("message", out message)
                ? message.GetString() ?? string.Empty
                : string.Empty;
        }
        catch { return json; }
    }

    private static int Base64Size(string value)
    {
        try
        {
            var normalized = value.Contains(',') ? value[(value.IndexOf(',') + 1)..] : value;
            return Convert.FromBase64String(normalized).Length;
        }
        catch { return -1; }
    }

    private async Task<LicenseCredentialPolicyDTO> GetPolicyAsync(Guid licenseId)
    {
        var policy = await _context.LicenseCredentialPolicies.FirstOrDefaultAsync(x => x.LicenseId == licenseId);
        if (policy is not null) return policy;
        policy = new LicenseCredentialPolicyDTO { LicenseId = licenseId, UpdatedAt = DateTime.UtcNow };
        _context.LicenseCredentialPolicies.Add(policy);
        await _context.SaveChangesAsync();
        return policy;
    }

    private static DateTime ResolveValidTo(CreateCredentialIn input, ResidentAccessDTO resident, LicenseCredentialPolicyDTO policy, DateTime validFrom, bool isTemporary)
    {
        if (input.Type == AccessCredentialTypeEnum.QrCode)
            return validFrom.AddMinutes(policy.QrCodeValidityMinutes);

        if (input.Type == AccessCredentialTypeEnum.Face && isTemporary)
        {
            var requested = NormalizeUtc(input.ValidTo ?? validFrom.AddMinutes(policy.TemporaryFaceValidityMinutes));
            var maximum = validFrom.AddMinutes(policy.MaxTemporaryFaceValidityMinutes);
            if (resident.Temporary && resident.Expire > validFrom && resident.Expire < maximum)
                maximum = resident.Expire;
            return requested > maximum ? maximum : requested;
        }

        return NormalizeUtc(input.ValidTo ?? (resident.Temporary ? resident.Expire : validFrom.AddYears(10)));
    }

    private async Task<string> GenerateQrIdentifierAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var bytes = RandomNumberGenerator.GetBytes(sizeof(ulong));
            var value = BitConverter.ToUInt64(bytes) & ((1UL << 40) - 1);
            if (value == 0) continue;
            var identifier = value.ToString();
            if (!await _context.ResidentAccessCredentials.AnyAsync(x => x.CredentialType == AccessCredentialTypeEnum.QrCode && x.Identifier == identifier))
                return identifier;
        }
        throw new InvalidOperationException("Nao foi possivel gerar um QR Code unico. Tente novamente.");
    }

    private static DateTime NormalizeUtc(DateTime value) => value.ToCondotifyUtc();
}

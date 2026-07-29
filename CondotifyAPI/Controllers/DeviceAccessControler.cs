using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.AccessControl;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

[ApiController]
[Authorize]
[Route("api/access/devices")]
public class DeviceAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IAccessControlService _controlService;
    private readonly ILicenseAuthorizationService _authorization;
    private readonly DatabaseContext _context;
    private readonly ILogger<DeviceAccessController> _logger;
    private readonly string? _apiKey;

    public DeviceAccessController(
        ISender sender,
        IAccessControlService controlService,
        ILicenseAuthorizationService authorization,
        DatabaseContext context,
        ILogger<DeviceAccessController> logger)
    {
        _sender = sender;
        _controlService = controlService;
        _authorization = authorization;
        _context = context;
        _logger = logger;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost("by-license")]
    public async Task<IActionResult> CreateByLicense(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        if (!Guid.TryParse(device.LicenseId, out var licenseId))
            return BadRequest(new { Result = "InvalidRequest", Errors = "A licenca informada e invalida." });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var command = device.ToCommand();
        var validator = await new CreateAccessControlDeviceByLicenseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });

        var activateWhenOnline = device.IsActive;
        command.MACAddress = null;
        command.SerialNumber = null;
        command.FirmwareVersion = null;
        var inspection = DeviceInspectionResult.Unavailable("O terminal nao respondeu ao inventario inicial.");
        try
        {
            inspection = await _controlService.InspectAsync(Probe(command));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha no inventário inicial do equipamento {DeviceName} da licença {LicenseId}", device.Name, licenseId);
        }
        var connectionTested = true;
        var connectionSucceeded = inspection.Online;
        command.IsActive = activateWhenOnline && connectionSucceeded;
        command.SerialNumber = Optional(inspection.SerialNumber);
        command.MACAddress = Optional(inspection.MacAddress)?.ToUpperInvariant();
        command.FirmwareVersion = Optional(inspection.FirmwareVersion);

        var result = await _sender.Send(command);
        if (result is null)
            return Conflict(new
            {
                Result = "DuplicateDevice",
                Errors = "Já existe um equipamento com o mesmo IP e porta, número de série ou endereço MAC nesta licença."
            });

        var persistedDevice = await _context.Devices.FirstOrDefaultAsync(x => x.Id == result.Id);
        if (persistedDevice is not null)
        {
            var now = DateTime.UtcNow;
            persistedDevice.LastHealthCheckAt = now;
            persistedDevice.LastSeenAt = connectionSucceeded ? now : null;
            persistedDevice.LastResponseTimeMs = inspection.ResponseTimeMs;
            persistedDevice.HealthMessage = inspection.Message;
            persistedDevice.CapacityJson = ValidJsonOrDefault(inspection.CapacityJson, "{}");
            persistedDevice.DiscoveredPortalsJson = JsonSerializer.Serialize(inspection.Portals);
            persistedDevice.LastUpdatedAt = now;
            _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "Device",
                EntityId = persistedDevice.Id,
                Action = "Created",
                Status = connectionSucceeded ? "Success" : "Offline",
                Summary = $"Equipamento {persistedDevice.Name} cadastrado.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    persistedDevice.Name,
                    persistedDevice.IPAddress,
                    persistedDevice.Port,
                    persistedDevice.Model,
                    persistedDevice.Type,
                    persistedDevice.IsActive,
                    ConnectionSucceeded = connectionSucceeded
                }),
                UserName = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? string.Empty,
                CreatedAt = now
            });
            await _context.SaveChangesAsync();
        }

        var message = connectionSucceeded
            ? command.IsActive
                ? "Equipamento identificado, salvo e ativado."
                : "Equipamento identificado e salvo como inativo."
            : "Equipamento salvo como inativo. Não foi possível consultar o inventário do terminal; revise a rede e as credenciais.";

        _logger.LogInformation(
            "Equipamento {DeviceName} salvo na licença {LicenseId}. Ativo: {IsActive}; teste inicial: {ConnectionSucceeded}",
            device.Name, licenseId, command.IsActive, connectionTested ? connectionSucceeded : null);

        return Created("", new CreateAccessControlDeviceByLicenseOut
        {
            Result = CreateAccessControlDeviceResult.Created,
            DeviceId = result.Id,
            DeviceName = result.Name,
            IsActive = command.IsActive,
            ConnectionTested = connectionTested,
            ConnectionSucceeded = connectionSucceeded,
            Message = message
        });
    }

    private static AccessControlDevice Probe(CreateAccessControlDeviceByLicenseCommand command) =>
        AccessControlDevice.Create(
            command.Name,
            command.IPAddress,
            command.Port,
            command.Username,
            command.Password,
            null,
            command.Model,
            null,
            null,
            command.Type,
            false,
            command.Location,
            DateTime.UtcNow,
            DateTime.UtcNow);

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ValidJsonOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        if (!Guid.TryParse(device.LicenseId, out var licenseId))
            return BadRequest(new { Result = "InvalidRequest", Errors = "A licenca informada e invalida." });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var ok = await _controlService.TestConnectionAsync(device);

        return ok
            ? Ok(new { Result = "Success" })
            : BadRequest(new { Result = "Fail" });
    }
}

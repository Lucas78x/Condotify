using System.Security.Claims;
using CondotifyAPI.Data.AccessControl;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/routes")]
[RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
public sealed class AccessRoutesController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IAccessRouteResolver _resolver;

    public AccessRoutesController(
        DatabaseContext context,
        IAccessRouteResolver resolver)
    {
        _context = context;
        _resolver = resolver;
    }

    [HttpGet]
    public async Task<IActionResult> GetRoutes(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var routes = await RouteQuery(licenseId)
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(routes.Select(ToOut));
    }

    [HttpGet("resolution/residents/{residentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
    public async Task<IActionResult> ResolveForResident(
        Guid licenseId,
        Guid residentId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var resident = await ResidentQuery(licenseId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == residentId);

        if (resident is null)
            return NotFound();

        var resolution = await _resolver.ResolveAsync(
            licenseId,
            resident,
            AccessCredentialTypeEnum.Face);

        return Ok(new ResidentRouteResolutionOut
        {
            Audience = (long)resolution.Audience,
            AudienceName = AudienceName(resolution.Audience),

            Routes = resolution.RouteNames
                .ToList(),

            Devices = resolution.Targets
                .Select(target => new ResolvedRouteDeviceOut
                {
                    DeviceId = target.Device.Id,
                    DeviceName = target.Device.Name,
                    DeviceType = target.Device.Type.ToString(),

                    RouteNames = target.Portals
                        .Select(x => x.RouteName)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList(),

                    Portals = target.Portals
                        .Select(x => x.PortalNumber)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList()
                })
                .ToList()
        });
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> CreateRoute(
        Guid licenseId,
        [FromBody] SaveAccessRouteIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = await ValidateAsync(licenseId, input);

        if (validation is not null)
        {
            return BadRequest(new
            {
                Result = "InvalidRoute",
                Errors = validation
            });
        }

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.AccessRoutes
            .AsNoTracking()
            .AnyAsync(x =>
                x.LicenseId == licenseId &&
                x.Name == normalizedName);

        if (duplicateExists)
        {
            return Conflict(new
            {
                Result = "DuplicateRoute",
                Errors = "Ja existe uma rota com este nome."
            });
        }

        var now = DateTime.UtcNow;

        var route = new AccessRouteDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyRoute(route, input);

        route.Devices = CreateRouteDevices(
            route.Id,
            input);

        _context.AccessRoutes.Add(route);

        QueueReconciliation(
            licenseId,
            $"Rota {route.Name} criada");

        AddRouteAudit(
            licenseId,
            route.Id,
            "Create",
            route.Name);

        await _context.SaveChangesAsync();

        var createdRoute = await RouteQuery(licenseId)
            .AsNoTracking()
            .FirstAsync(x => x.Id == route.Id);

        return Created(
            string.Empty,
            ToOut(createdRoute));
    }

    [HttpPut("{routeId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> UpdateRoute(
        Guid licenseId,
        Guid routeId,
        [FromBody] SaveAccessRouteIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = await ValidateAsync(licenseId, input);

        if (validation is not null)
        {
            return BadRequest(new
            {
                Result = "InvalidRoute",
                Errors = validation
            });
        }

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.AccessRoutes
            .AsNoTracking()
            .AnyAsync(x =>
                x.LicenseId == licenseId &&
                x.Id != routeId &&
                x.Name == normalizedName);

        if (duplicateExists)
        {
            return Conflict(new
            {
                Result = "DuplicateRoute",
                Errors = "Ja existe uma rota com este nome."
            });
        }

        /*
         * Importante:
         * Não carregamos route.Devices com Include().
         * Os vínculos serão removidos diretamente no banco.
         */
        var route = await _context.AccessRoutes
            .FirstOrDefaultAsync(x =>
                x.Id == routeId &&
                x.LicenseId == licenseId);

        if (route is null)
            return NotFound();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            /*
             * Remove todos os vínculos antigos diretamente no banco.
             *
             * Isso evita:
             * - RemoveRange em entidades desatualizadas;
             * - coleção rastreada inconsistente;
             * - exclusão duplicada;
             * - DbUpdateConcurrencyException nos dispositivos antigos.
             */
            await _context.AccessRouteDevices
                .Where(x => x.AccessRouteId == routeId)
                .ExecuteDeleteAsync();

            /*
             * Atualiza apenas os dados principais da rota.
             */
            ApplyRoute(route, input);

            route.UpdatedAt = DateTime.UtcNow;

            /*
             * Cria novos vínculos.
             */
            var newDevices = CreateRouteDevices(
                route.Id,
                input);

            await _context.AccessRouteDevices
                .AddRangeAsync(newDevices);

            QueueReconciliation(
                licenseId,
                $"Rota {route.Name} atualizada");

            AddRouteAudit(
                licenseId,
                route.Id,
                "Update",
                route.Name);

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();

            return Conflict(new
            {
                Result = "ConcurrencyConflict",
                Errors =
                    "A rota foi alterada ou excluida por outro processo. Atualize a pagina e tente novamente.",

                Entities = ex.Entries
                    .Select(x => x.Metadata.ClrType.Name)
                    .Distinct()
                    .ToList()
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var updatedRoute = await RouteQuery(licenseId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == routeId);

        if (updatedRoute is null)
            return NotFound();

        return Ok(ToOut(updatedRoute));
    }

    [HttpDelete("{routeId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> DeleteRoute(
        Guid licenseId,
        Guid routeId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var route = await _context.AccessRoutes
            .FirstOrDefaultAsync(x =>
                x.Id == routeId &&
                x.LicenseId == licenseId);

        if (route is null)
            return NotFound();

        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            /*
             * Remove os vínculos explicitamente antes da rota.
             * Isso também evita problemas caso o cascade delete
             * não esteja configurado corretamente.
             */
            await _context.AccessRouteDevices
                .Where(x => x.AccessRouteId == routeId)
                .ExecuteDeleteAsync();

            QueueReconciliation(
                licenseId,
                $"Rota {route.Name} excluida");

            AddRouteAudit(
                licenseId,
                route.Id,
                "Delete",
                route.Name);

            _context.AccessRoutes.Remove(route);

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync();

            return Conflict(new
            {
                Result = "ConcurrencyConflict",
                Errors =
                    "A rota ja foi alterada ou excluida por outro processo."
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private IQueryable<AccessRouteDTO> RouteQuery(Guid licenseId)
    {
        return _context.AccessRoutes
            .Include(x => x.Devices)
            .ThenInclude(x => x.Device)
            .Where(x => x.LicenseId == licenseId);
    }

    private IQueryable<ResidentAccessDTO> ResidentQuery(Guid licenseId)
    {
        return _context.Residents
            .Include(x => x.Unit)
            .ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .ForLicense(licenseId);
    }

    private async Task<string?> ValidateAsync(
        Guid licenseId,
        SaveAccessRouteIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Informe o nome da rota.";

        if (input.Name.Trim().Length > 120)
            return "O nome da rota deve ter ate 120 caracteres.";

        if (input.Audience == AccessRouteAudienceEnum.None ||
            (input.Audience & ~AccessRouteAudienceEnum.All) != 0)
        {
            return "Selecione ao menos um publico valido.";
        }

        if (input.DaysOfWeekMask is < 1 or > 127)
            return "Selecione ao menos um dia da semana.";

        if (input.StartTime < TimeSpan.Zero ||
            input.EndTime > TimeSpan.FromDays(1) ||
            input.StartTime >= input.EndTime)
        {
            return
                "A janela de acesso deve possuir horario inicial anterior ao horario final.";
        }

        if (input.Devices is null || input.Devices.Count == 0)
            return "Vincule ao menos um equipamento a rota.";

        if (input.Devices.Any(x =>
                x.DeviceId == Guid.Empty ||
                x.PortalNumber is < 1 or > 128 ||
                !Enum.IsDefined(x.Direction)))
        {
            return
                "Existe um equipamento, portal ou sentido invalido na rota.";
        }

        if (input.Devices
            .GroupBy(x => new
            {
                x.DeviceId,
                x.PortalNumber
            })
            .Any(x => x.Count() > 1))
        {
            return
                "O mesmo equipamento e portal nao pode ser repetido na rota.";
        }

        var deviceIds = input.Devices
            .Select(x => x.DeviceId)
            .Distinct()
            .ToList();

        var existingCount = await _context.Devices
            .AsNoTracking()
            .CountAsync(x =>
                x.LicenseId == licenseId &&
                deviceIds.Contains(x.Id));

        return existingCount == deviceIds.Count
            ? null
            : "Um ou mais equipamentos nao pertencem a esta licenca.";
    }

    /// <summary>
    /// Atualiza somente os campos principais da rota.
    /// Não altera a coleção Devices.
    /// </summary>
    private static void ApplyRoute(
        AccessRouteDTO route,
        SaveAccessRouteIn input)
    {
        route.Name = input.Name.Trim();
        route.Description = input.Description?.Trim() ?? string.Empty;
        route.Audience = input.Audience;
        route.IsActive = input.IsActive;
        route.AllowTemporary = input.AllowTemporary;
        route.DaysOfWeekMask = input.DaysOfWeekMask;
        route.StartTime = input.StartTime;
        route.EndTime = input.EndTime;
    }

    /// <summary>
    /// Cria os vínculos da rota com os equipamentos.
    /// </summary>
    private static List<AccessRouteDeviceDTO> CreateRouteDevices(
        Guid routeId,
        SaveAccessRouteIn input)
    {
        return input.Devices
            .Select(item => new AccessRouteDeviceDTO
            {
                Id = Guid.NewGuid(),
                AccessRouteId = routeId,
                DeviceId = item.DeviceId,
                PortalNumber = item.PortalNumber,
                Direction = item.Direction,
                IsActive = item.IsActive
            })
            .ToList();
    }

    private static AccessRouteOut ToOut(AccessRouteDTO route)
    {
        return new AccessRouteOut
        {
            Id = route.Id,
            Name = route.Name,
            Description = route.Description,
            Audience = (long)route.Audience,
            IsActive = route.IsActive,
            AllowTemporary = route.AllowTemporary,
            DaysOfWeekMask = route.DaysOfWeekMask,
            StartTime = route.StartTime,
            EndTime = route.EndTime,

            Devices = route.Devices
                .OrderBy(x => x.Device.Name)
                .ThenBy(x => x.PortalNumber)
                .Select(x => new AccessRouteDeviceOut
                {
                    Id = x.Id,
                    DeviceId = x.DeviceId,
                    DeviceName = x.Device.Name,
                    DeviceType = x.Device.Type.ToString(),
                    PortalNumber = x.PortalNumber,
                    Direction = x.Direction.ToString(),
                    IsActive = x.IsActive
                })
                .ToList()
        };
    }

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim =
            User.FindFirstValue("enterprise_id");

        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return false;

        return await _context.Licenses
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == licenseId &&
                x.EnterpriseId == enterpriseId);
    }

    private void QueueReconciliation(
        Guid licenseId,
        string reason)
    {
        _context.AccessBatchOperations.Add(
            new AccessBatchOperationDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                Operation = "ReconcileCredentials",
                Status = AccessBatchStatusEnum.Queued,

                RequestedBy =
                    User.FindFirstValue("name") ??
                    User.Identity?.Name ??
                    "Usuario do portal",

                FilterJson = "{}",
                Error = reason,
                CreatedAt = DateTime.UtcNow
            });
    }

    private void AddRouteAudit(
        Guid licenseId,
        Guid routeId,
        string action,
        string routeName)
    {
        _context.AccessOperationAudits.Add(
            new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "AccessRoute",
                EntityId = routeId,
                Action = action,
                Status = "Queued",

                Summary =
                    $"Rota {routeName}: reconciliacao automatica agendada.",

                DetailsJson = "{}",

                UserName =
                    User.FindFirstValue("name") ??
                    User.Identity?.Name ??
                    "Usuario do portal",

                CreatedAt = DateTime.UtcNow
            });
    }

    private static string AudienceName(
        AccessRouteAudienceEnum audience)
    {
        return audience switch
        {
            AccessRouteAudienceEnum.OwnerResponsible =>
                "Proprietario responsavel",

            AccessRouteAudienceEnum.TenantResponsible =>
                "Locatario responsavel",

            AccessRouteAudienceEnum.Responsible =>
                "Responsavel",

            AccessRouteAudienceEnum.Dependent =>
                "Dependente",

            AccessRouteAudienceEnum.Visitor =>
                "Visitante",

            AccessRouteAudienceEnum.ServiceProvider =>
                "Prestador",

            _ => "Morador"
        };
    }
}
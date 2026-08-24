using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Governance;
using CondotifyAPI.Domain.Enums.Governance;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiMobileNotificationCategory = CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/assemblies")]
public sealed class AssembliesController(
    DatabaseContext context,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewAssemblies)]
    public async Task<IActionResult> List(Guid licenseId, CancellationToken cancellationToken)
    {
        var assemblies = await BaseQuery(context).AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.StartsAt)
            .ToListAsync(cancellationToken);
        return Ok(assemblies.Select(x => AssemblyProjection.ToSummary(x)));
    }

    [HttpGet("{assemblyId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ViewAssemblies)]
    public async Task<IActionResult> Get(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        return assembly is null
            ? NotFound()
            : Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpGet("{assemblyId:guid}/audit")]
    [RequireLicensePermission(LicensePermissionEnum.ViewAssemblies)]
    public async Task<IActionResult> Audit(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var exists = await context.CondominiumAssemblies.AsNoTracking()
            .AnyAsync(x => x.Id == assemblyId && x.LicenseId == licenseId, cancellationToken);
        if (!exists) return NotFound();
        var events = await context.AssemblyAudits.AsNoTracking()
            .Where(x => x.AssemblyId == assemblyId && x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AssemblyAuditViewModel
            {
                Id = x.Id, EventType = x.EventType, ActorType = x.ActorType,
                ActorName = x.ActorName, CreatedAt = x.CreatedAt
            }).ToListAsync(cancellationToken);
        return Ok(events);
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Create(
        Guid licenseId,
        [FromBody] AssemblyFormViewModel input,
        CancellationToken cancellationToken)
    {
        var error = AssemblyRules.Validate(input);
        if (error is not null) return BadRequest(new { Errors = error });

        var now = DateTime.UtcNow;
        var assembly = CreateCore(licenseId, input, ActorName(), now);
        context.CondominiumAssemblies.Add(assembly);
        AddAudit(assembly, "Created", new { assembly.Type, assembly.Format, AgendaItems = assembly.AgendaItems.Count });
        await context.SaveChangesAsync(cancellationToken);
        return Created(string.Empty, AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpPut("{assemblyId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Update(
        Guid licenseId,
        Guid assemblyId,
        [FromBody] AssemblyFormViewModel input,
        CancellationToken cancellationToken)
    {
        var error = AssemblyRules.Validate(input);
        if (error is not null) return BadRequest(new { Errors = error });
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status != AssemblyStatusEnum.Draft)
            return Conflict(new { Errors = "Somente rascunhos podem ser alterados." });

        Apply(assembly, input, DateTime.UtcNow);
        AddAudit(assembly, "Updated", new { AgendaItems = assembly.AgendaItems.Count });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpPost("{assemblyId:guid}/publish")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Publish(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status != AssemblyStatusEnum.Draft)
            return Conflict(new { Errors = "Apenas um rascunho pode ser publicado." });
        var error = AssemblyRules.ValidateEntity(assembly);
        if (error is not null) return BadRequest(new { Errors = error });
        if (assembly.VotingEndsAt <= DateTime.UtcNow)
            return Conflict(new { Errors = "Atualize o período de votação antes de publicar." });

        var units = await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (units.Count == 0)
            return Conflict(new { Errors = "Cadastre ao menos uma unidade antes de publicar." });

        var now = DateTime.UtcNow;
        foreach (var unitId in units)
            assembly.EligibleUnits.Add(new AssemblyEligibleUnitDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, UnitId = unitId,
                Weight = 1m, IsEligible = true, CreatedAt = now
            });
        assembly.Status = AssemblyStatusEnum.Published;
        assembly.PublishedAt = now;
        assembly.UpdatedAt = now;
        AddAudit(assembly, "Published", new { EligibleUnits = units.Count });
        await context.SaveChangesAsync(cancellationToken);

        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        foreach (var residentId in ResourceDocumentsController.ResolveLicenseNotificationTargets(links, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                ApiMobileNotificationCategory.Announcement,
                assembly.Type == AssemblyTypeEnum.Poll ? "Nova enquete" : "Nova assembleia",
                assembly.Title,
                $"/assembleias/{assembly.Id}",
                $"assembly-published:{assembly.Id:N}",
                cancellationToken);
        }

        return Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpPost("{assemblyId:guid}/open")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Open(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status != AssemblyStatusEnum.Published)
            return Conflict(new { Errors = "A assembleia precisa estar publicada para ser aberta." });
        if (!assembly.EligibleUnits.Any(x => x.IsEligible))
            return Conflict(new { Errors = "A assembleia não possui unidades elegíveis." });

        var now = DateTime.UtcNow;
        if (now >= assembly.VotingEndsAt)
            return Conflict(new { Errors = "O período de votação já foi encerrado." });
        assembly.Status = AssemblyStatusEnum.Open;
        assembly.OpenedAt = now;
        assembly.UpdatedAt = now;
        AddAudit(assembly, "Opened", new { assembly.VotingStartsAt, assembly.VotingEndsAt });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpPost("{assemblyId:guid}/close")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Close(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status != AssemblyStatusEnum.Open)
            return Conflict(new { Errors = "Somente uma assembleia aberta pode ser encerrada." });

        var now = DateTime.UtcNow;
        assembly.Status = AssemblyStatusEnum.Closed;
        assembly.ClosedAt = now;
        assembly.UpdatedAt = now;
        AddAudit(assembly, "Closed", new
        {
            Attendances = assembly.Attendances.Count,
            Votes = assembly.Votes.Count,
            Results = assembly.AgendaItems.Select(item => AssemblyProjection.ResultSummary(item, assembly))
        });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpPost("{assemblyId:guid}/cancel")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Cancel(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await LoadAsync(licenseId, assemblyId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status is AssemblyStatusEnum.Closed or AssemblyStatusEnum.Cancelled)
            return Conflict(new { Errors = "Esta assembleia já foi finalizada." });

        var previousState = assembly.Status;
        var now = DateTime.UtcNow;
        assembly.Status = AssemblyStatusEnum.Cancelled;
        assembly.CancelledAt = now;
        assembly.UpdatedAt = now;
        AddAudit(assembly, "Cancelled", new { PreviousState = previousState.ToString() });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(AssemblyProjection.ToDetail(assembly, null, AssemblyRules.ResultsVisible(assembly), []));
    }

    [HttpDelete("{assemblyId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAssemblies)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken)
    {
        var assembly = await context.CondominiumAssemblies
            .FirstOrDefaultAsync(x => x.Id == assemblyId && x.LicenseId == licenseId, cancellationToken);
        if (assembly is null) return NotFound();
        if (assembly.Status != AssemblyStatusEnum.Draft)
            return Conflict(new { Errors = "Somente rascunhos podem ser excluídos." });
        context.CondominiumAssemblies.Remove(assembly);
        await context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    internal static CondominiumAssemblyDTO CreateCore(
        Guid licenseId,
        AssemblyFormViewModel input,
        string actor,
        DateTime now)
    {
        var assembly = new CondominiumAssemblyDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, Status = AssemblyStatusEnum.Draft,
            CreatedBy = actor, CreatedAt = now, UpdatedAt = now
        };
        Apply(assembly, input, now);
        return assembly;
    }

    internal static void Apply(CondominiumAssemblyDTO assembly, AssemblyFormViewModel input, DateTime now)
    {
        assembly.Title = input.Title.Trim();
        assembly.Description = input.Description?.Trim() ?? string.Empty;
        assembly.Type = (AssemblyTypeEnum)input.Type;
        assembly.Format = (AssemblyFormatEnum)input.Format;
        assembly.VoteVisibility = (AssemblyVoteVisibilityEnum)input.VoteVisibility;
        assembly.Location = input.Location?.Trim() ?? string.Empty;
        assembly.MeetingUrl = input.MeetingUrl?.Trim() ?? string.Empty;
        assembly.StartsAt = input.StartsAt.ToUniversalTime();
        assembly.VotingStartsAt = input.VotingStartsAt.ToUniversalTime();
        assembly.VotingEndsAt = input.VotingEndsAt.ToUniversalTime();
        assembly.AllowVoteChange = input.AllowVoteChange;
        assembly.ShowResultsBeforeClose = input.ShowResultsBeforeClose;
        assembly.RequireResponsibleResident = input.RequireResponsibleResident;
        assembly.UpdatedAt = now;

        assembly.AgendaItems.Clear();
        for (var itemIndex = 0; itemIndex < input.AgendaItems.Count; itemIndex++)
        {
            var source = input.AgendaItems[itemIndex];
            var item = new AssemblyAgendaItemDTO
            {
                Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, Order = itemIndex + 1,
                Title = source.Title.Trim(), Description = source.Description?.Trim() ?? string.Empty,
                QuorumPercentage = source.QuorumPercentage, ApprovalPercentage = source.ApprovalPercentage,
                AbstentionCountsForQuorum = source.AbstentionCountsForQuorum,
                CreatedAt = now, UpdatedAt = now
            };
            for (var optionIndex = 0; optionIndex < source.Options.Count; optionIndex++)
            {
                var option = source.Options[optionIndex];
                item.Options.Add(new AssemblyVoteOptionDTO
                {
                    Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, Order = optionIndex + 1,
                    Label = option.Label.Trim(), IsApproval = option.IsApproval,
                    IsAbstention = option.IsAbstention, CreatedAt = now
                });
            }
            assembly.AgendaItems.Add(item);
        }
    }

    private async Task<CondominiumAssemblyDTO?> LoadAsync(Guid licenseId, Guid assemblyId, CancellationToken cancellationToken) =>
        await BaseQuery(context).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == assemblyId && x.LicenseId == licenseId, cancellationToken);

    internal static IQueryable<CondominiumAssemblyDTO> BaseQuery(DatabaseContext database) =>
        database.CondominiumAssemblies
            .Include(x => x.AgendaItems).ThenInclude(x => x.Options)
            .Include(x => x.AgendaItems).ThenInclude(x => x.Votes)
            .Include(x => x.EligibleUnits).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Attendances).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Attendances).ThenInclude(x => x.Resident)
            .Include(x => x.Votes).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Votes).ThenInclude(x => x.Resident);

    private string ActorName() => User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administração";

    private void AddAudit(CondominiumAssemblyDTO assembly, string eventType, object details)
    {
        Guid? actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : null;
        assembly.Audits.Add(new AssemblyAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, EventType = eventType,
            ActorType = "Staff", ActorId = actorId, ActorName = ActorName(),
            DetailsJson = JsonSerializer.Serialize(details), CreatedAt = DateTime.UtcNow
        });
    }
}

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/assemblies")]
public sealed class ResidentAssembliesController(
    DatabaseContext context,
    IResidentAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        var assemblies = await AssembliesController.BaseQuery(context).AsNoTracking().AsSplitQuery()
            .Where(x => x.LicenseId == grant.LicenseId && x.Status != AssemblyStatusEnum.Draft)
            .OrderByDescending(x => x.Status == AssemblyStatusEnum.Open)
            .ThenByDescending(x => x.StartsAt)
            .ToListAsync(cancellationToken);
        return Ok(assemblies.Select(x => AssemblyProjection.ToSummary(x, grant.ResidentId)));
    }

    [HttpGet("{assemblyId:guid}")]
    public async Task<IActionResult> Get(Guid assemblyId, CancellationToken cancellationToken)
    {
        var access = await LoadResidentAccessAsync(assemblyId, cancellationToken);
        if (access is null) return NotFound();
        return Ok(AssemblyProjection.ToDetail(
            access.Assembly,
            access.Grant.ResidentId,
            AssemblyRules.ResultsVisible(access.Assembly),
            access.Units));
    }

    [HttpPost("{assemblyId:guid}/attendance")]
    public async Task<IActionResult> Attend(
        Guid assemblyId,
        [FromBody] AssemblyUnitAction input,
        CancellationToken cancellationToken)
    {
        var access = await LoadResidentAccessAsync(assemblyId, cancellationToken);
        if (access is null) return NotFound();
        if (access.Assembly.Status is not (AssemblyStatusEnum.Published or AssemblyStatusEnum.Open))
            return Conflict(new { Errors = "A lista de presença não está aberta." });
        var unit = access.Units.FirstOrDefault(x => x.UnitId == input.UnitId);
        if (unit is null || !unit.CanVote)
            return Forbid();

        var existing = access.Assembly.Attendances.FirstOrDefault(x => x.UnitId == input.UnitId);
        if (existing is not null)
        {
            if (existing.ResidentId != access.Grant.ResidentId)
                return Conflict(new { Errors = "Esta unidade já possui representante na lista de presença." });
            return Ok(AssemblyProjection.ToDetail(access.Assembly, access.Grant.ResidentId,
                AssemblyRules.ResultsVisible(access.Assembly), access.Units));
        }

        AddAttendance(access, input.UnitId);
        AddResidentAudit(access.Assembly, access.Grant.ResidentId, "AttendanceRegistered", new { input.UnitId });
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var concurrent = await LoadResidentAccessAsync(assemblyId, cancellationToken);
            var attendance = concurrent?.Assembly.Attendances.FirstOrDefault(x => x.UnitId == input.UnitId);
            if (concurrent is not null && attendance?.ResidentId == concurrent.Grant.ResidentId)
                return Ok(AssemblyProjection.ToDetail(concurrent.Assembly, concurrent.Grant.ResidentId,
                    AssemblyRules.ResultsVisible(concurrent.Assembly), concurrent.Units));
            return Conflict(new { Errors = "Esta unidade já possui representante na lista de presença." });
        }
        return Ok(AssemblyProjection.ToDetail(access.Assembly, access.Grant.ResidentId,
            AssemblyRules.ResultsVisible(access.Assembly), access.Units));
    }

    [HttpPost("{assemblyId:guid}/agenda/{agendaItemId:guid}/vote")]
    public async Task<IActionResult> Vote(
        Guid assemblyId,
        Guid agendaItemId,
        [FromBody] CastAssemblyVoteViewModel input,
        CancellationToken cancellationToken)
    {
        var access = await LoadResidentAccessAsync(assemblyId, cancellationToken);
        if (access is null) return NotFound();
        var now = DateTime.UtcNow;
        var votingError = AssemblyRules.VotingError(access.Assembly, now);
        if (votingError is not null) return Conflict(new { Errors = votingError });

        var unit = access.Units.FirstOrDefault(x => x.UnitId == input.UnitId);
        if (unit is null || !unit.CanVote) return Forbid();
        var item = access.Assembly.AgendaItems.FirstOrDefault(x => x.Id == agendaItemId);
        if (item is null) return NotFound();
        var option = item.Options.FirstOrDefault(x => x.Id == input.OptionId);
        if (option is null) return BadRequest(new { Errors = "Selecione uma opção válida para esta pauta." });

        var existing = item.Votes.FirstOrDefault(x => x.UnitId == input.UnitId);
        if (existing is not null)
        {
            if (existing.ResidentId != access.Grant.ResidentId)
                return Conflict(new { Errors = "Esta unidade já votou nesta pauta com outro representante." });
            if (!access.Assembly.AllowVoteChange && existing.OptionId != option.Id)
                return Conflict(new { Errors = "Esta votação não permite alterar o voto." });
            if (existing.OptionId != option.Id)
            {
                existing.OptionId = option.Id;
                existing.Revision++;
                existing.UpdatedAt = now;
                AddResidentAudit(access.Assembly, access.Grant.ResidentId, "VoteChanged",
                    new { AgendaItemId = item.Id, input.UnitId, existing.Revision });
            }
        }
        else
        {
            var vote = new AssemblyVoteDTO
            {
                Id = Guid.NewGuid(), LicenseId = access.Assembly.LicenseId,
                AssemblyId = access.Assembly.Id, AgendaItemId = item.Id, OptionId = option.Id,
                UnitId = input.UnitId, ResidentId = access.Grant.ResidentId, Weight = unit.Weight,
                Revision = 1, CastAt = now, UpdatedAt = now
            };
            item.Votes.Add(vote);
            access.Assembly.Votes.Add(vote);
            AddResidentAudit(access.Assembly, access.Grant.ResidentId, "VoteCast",
                new { AgendaItemId = item.Id, input.UnitId, Revision = 1 });
        }

        if (access.Assembly.Attendances.All(x => x.UnitId != input.UnitId)) AddAttendance(access, input.UnitId);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var concurrent = await LoadResidentAccessAsync(assemblyId, cancellationToken);
            var savedVote = concurrent?.Assembly.AgendaItems.FirstOrDefault(x => x.Id == agendaItemId)?.Votes
                .FirstOrDefault(x => x.UnitId == input.UnitId);
            if (concurrent is not null && savedVote?.ResidentId == concurrent.Grant.ResidentId && savedVote.OptionId == input.OptionId)
                return Ok(AssemblyProjection.ToDetail(concurrent.Assembly, concurrent.Grant.ResidentId,
                    AssemblyRules.ResultsVisible(concurrent.Assembly), concurrent.Units));
            return Conflict(new { Errors = savedVote is null
                ? "O voto concorreu com outra ação. Atualize a pauta e tente novamente."
                : "Esta unidade já registrou um voto para a pauta." });
        }

        return Ok(AssemblyProjection.ToDetail(access.Assembly, access.Grant.ResidentId,
            AssemblyRules.ResultsVisible(access.Assembly), access.Units));
    }

    private async Task<ResidentAssemblyAccess?> LoadResidentAccessAsync(Guid assemblyId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return null;
        var assembly = await AssembliesController.BaseQuery(context).AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == assemblyId && x.LicenseId == grant.LicenseId && x.Status != AssemblyStatusEnum.Draft, cancellationToken);
        if (assembly is null) return null;

        var now = DateTime.UtcNow;
        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(x => x.ResidentId == grant.ResidentId && x.Unit.Block.LicenseId == grant.LicenseId)
            .ToListAsync(cancellationToken);
        var eligible = assembly.EligibleUnits.Where(x => x.IsEligible).ToDictionary(x => x.UnitId);
        var units = links.Where(x => ResidentAuthorizationService.LinkIsCurrentlyValid(x, now))
            .GroupBy(x => x.UnitId)
            .Select(group =>
            {
                var link = group.OrderByDescending(x => x.IsPrimary).First();
                var responsible = group.Any(x => x.Relationship is ResidentUnitRelationshipEnum.OwnerResponsible
                    or ResidentUnitRelationshipEnum.TenantResponsible or ResidentUnitRelationshipEnum.Responsible);
                var isEligible = eligible.TryGetValue(group.Key, out var snapshot);
                var canVote = isEligible && (!assembly.RequireResponsibleResident || responsible);
                return new AssemblyResidentUnitViewModel
                {
                    UnitId = group.Key,
                    Label = $"{link.Unit.Block.Name} / {link.Unit.Number}",
                    Weight = snapshot?.Weight ?? 0,
                    CanVote = canVote,
                    Reason = !isEligible ? "Unidade fora da lista de elegibilidade."
                        : !canVote ? "O voto está restrito ao responsável pela unidade." : string.Empty
                };
            }).OrderBy(x => x.Label).ToList();
        return new ResidentAssemblyAccess(assembly, grant, units);
    }

    private static void AddAttendance(ResidentAssemblyAccess access, Guid unitId)
    {
        access.Assembly.Attendances.Add(new AssemblyAttendanceDTO
        {
            Id = Guid.NewGuid(), LicenseId = access.Assembly.LicenseId,
            AssemblyId = access.Assembly.Id, UnitId = unitId,
            ResidentId = access.Grant.ResidentId, JoinedAt = DateTime.UtcNow
        });
    }

    private void AddResidentAudit(CondominiumAssemblyDTO assembly, Guid residentId, string eventType, object details)
    {
        assembly.Audits.Add(new AssemblyAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, EventType = eventType,
            ActorType = "Resident", ActorId = residentId,
            ActorName = User.FindFirstValue("name") ?? "Morador",
            DetailsJson = JsonSerializer.Serialize(details), CreatedAt = DateTime.UtcNow
        });
    }

    private sealed record ResidentAssemblyAccess(
        CondominiumAssemblyDTO Assembly,
        ResidentAccessGrant Grant,
        List<AssemblyResidentUnitViewModel> Units);
}

public sealed class AssemblyUnitAction
{
    public Guid UnitId { get; set; }
}

internal static class AssemblyRules
{
    internal static string? Validate(AssemblyFormViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 180)
            return "Informe um título com até 180 caracteres.";
        if ((input.Description?.Length ?? 0) > 8000) return "A descrição deve ter no máximo 8000 caracteres.";
        if ((input.Location?.Length ?? 0) > 300 || (input.MeetingUrl?.Length ?? 0) > 1000)
            return "Local ou link da reunião excede o tamanho permitido.";
        if (!Enum.IsDefined((AssemblyTypeEnum)input.Type) || !Enum.IsDefined((AssemblyFormatEnum)input.Format) ||
            !Enum.IsDefined((AssemblyVoteVisibilityEnum)input.VoteVisibility)) return "Selecione tipo, formato e visibilidade válidos.";
        if (input.VotingEndsAt <= input.VotingStartsAt) return "O encerramento da votação deve ocorrer depois da abertura.";
        if (input.AgendaItems is null || input.AgendaItems.Count is < 1 or > 30) return "Informe entre 1 e 30 pautas.";
        if ((AssemblyFormatEnum)input.Format is AssemblyFormatEnum.InPerson or AssemblyFormatEnum.Hybrid && string.IsNullOrWhiteSpace(input.Location))
            return "Informe o local da assembleia presencial ou híbrida.";
        if ((AssemblyFormatEnum)input.Format is AssemblyFormatEnum.Virtual or AssemblyFormatEnum.Hybrid &&
            (!Uri.TryCreate(input.MeetingUrl, UriKind.Absolute, out var meetingUri) || meetingUri.Scheme != Uri.UriSchemeHttps))
            return "Informe um link HTTPS válido para a participação remota.";

        foreach (var item in input.AgendaItems)
        {
            if (item is null) return "Informe uma pauta válida.";
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Trim().Length > 240)
                return "Toda pauta precisa de um título com até 240 caracteres.";
            if ((item.Description?.Length ?? 0) > 6000)
                return "A descrição da pauta deve ter no máximo 6000 caracteres.";
            if (item.QuorumPercentage is < 0 or > 100 || item.ApprovalPercentage is < 0 or > 100)
                return "Quórum e aprovação devem ficar entre 0% e 100%.";
            if (item.Options is null || item.Options.Count is < 2 or > 12) return "Cada pauta deve possuir entre 2 e 12 opções.";
            if (item.Options.Any(x => x is null || string.IsNullOrWhiteSpace(x.Label) || x.Label.Trim().Length > 180))
                return "As opções devem possuir texto com até 180 caracteres.";
            if (item.Options.Any(x => x.IsApproval && x.IsAbstention))
                return "Uma opção de abstenção não pode ser marcada como favorável.";
            if (item.Options.Select(x => x.Label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Options.Count)
                return "As opções de uma pauta não podem ter textos repetidos.";
            if (item.Options.Count(x => x.IsAbstention) > 1) return "Cada pauta pode ter no máximo uma opção de abstenção.";
            if (item.Options.All(x => !x.IsApproval) && input.Type != (int)AssemblyTypeEnum.Poll)
                return "Pautas formais precisam indicar ao menos uma opção favorável.";
        }
        return null;
    }

    internal static string? ValidateEntity(CondominiumAssemblyDTO assembly)
    {
        if (assembly.AgendaItems.Count == 0) return "Inclua ao menos uma pauta antes de publicar.";
        if (assembly.AgendaItems.Any(x => x.Options.Count < 2)) return "Todas as pautas precisam de ao menos duas opções.";
        return null;
    }

    internal static string? VotingError(CondominiumAssemblyDTO assembly, DateTime now)
    {
        if (assembly.Status != AssemblyStatusEnum.Open) return "A votação não está aberta.";
        if (now < assembly.VotingStartsAt) return "O período de votação ainda não começou.";
        if (now >= assembly.VotingEndsAt) return "O período de votação foi encerrado.";
        return null;
    }

    internal static bool ResultsVisible(CondominiumAssemblyDTO assembly) =>
        assembly.Status == AssemblyStatusEnum.Closed ||
        assembly.Status == AssemblyStatusEnum.Open && assembly.ShowResultsBeforeClose;
}

internal static class AssemblyProjection
{
    internal static AssemblySummaryViewModel ToSummary(CondominiumAssemblyDTO assembly, Guid? residentId = null) => new()
    {
        Id = assembly.Id, Title = assembly.Title, Description = assembly.Description,
        Type = assembly.Type switch { AssemblyTypeEnum.Ordinary => "Ordinária", AssemblyTypeEnum.Extraordinary => "Extraordinária", _ => "Enquete" },
        Format = assembly.Format switch { AssemblyFormatEnum.InPerson => "Presencial", AssemblyFormatEnum.Hybrid => "Híbrida", _ => "Virtual" },
        Status = assembly.Status switch { AssemblyStatusEnum.Draft => "Rascunho", AssemblyStatusEnum.Published => "Publicada", AssemblyStatusEnum.Open => "Aberta", AssemblyStatusEnum.Closed => "Encerrada", _ => "Cancelada" },
        StartsAt = assembly.StartsAt, VotingStartsAt = assembly.VotingStartsAt, VotingEndsAt = assembly.VotingEndsAt,
        AgendaItemCount = assembly.AgendaItems.Count,
        EligibleUnitCount = assembly.EligibleUnits.Count(x => x.IsEligible),
        AttendanceCount = assembly.Attendances.Count,
        VoteCount = assembly.Votes.Count,
        HasResidentVoted = residentId.HasValue && assembly.Votes.Any(x => x.ResidentId == residentId.Value)
    };

    internal static AssemblyDetailViewModel ToDetail(
        CondominiumAssemblyDTO assembly,
        Guid? residentId,
        bool resultsVisible,
        IReadOnlyCollection<AssemblyResidentUnitViewModel> availableUnits)
    {
        var summary = ToSummary(assembly, residentId);
        var eligibleWeight = assembly.EligibleUnits.Where(x => x.IsEligible).Sum(x => x.Weight);
        return new AssemblyDetailViewModel
        {
            Id = summary.Id, Title = summary.Title, Description = summary.Description,
            Type = summary.Type, Format = summary.Format, Status = summary.Status,
            StartsAt = summary.StartsAt, VotingStartsAt = summary.VotingStartsAt, VotingEndsAt = summary.VotingEndsAt,
            AgendaItemCount = summary.AgendaItemCount, EligibleUnitCount = summary.EligibleUnitCount,
            AttendanceCount = summary.AttendanceCount, VoteCount = summary.VoteCount,
            HasResidentVoted = summary.HasResidentVoted,
            Location = assembly.Location, MeetingUrl = assembly.MeetingUrl,
            VoteVisibility = assembly.VoteVisibility == AssemblyVoteVisibilityEnum.Open ? "Aberto" : "Secreto", AllowVoteChange = assembly.AllowVoteChange,
            ShowResultsBeforeClose = assembly.ShowResultsBeforeClose,
            RequireResponsibleResident = assembly.RequireResponsibleResident,
            CreatedBy = assembly.CreatedBy, PublishedAt = assembly.PublishedAt,
            OpenedAt = assembly.OpenedAt, ClosedAt = assembly.ClosedAt,
            ResultsVisible = resultsVisible, EligibleWeight = eligibleWeight,
            AvailableUnits = availableUnits.ToList(),
            AgendaItems = assembly.AgendaItems.OrderBy(x => x.Order)
                .Select(item => ToAgenda(item, assembly, residentId, resultsVisible, eligibleWeight)).ToList(),
            Attendances = assembly.Attendances.OrderBy(x => x.JoinedAt).Select(x => new AssemblyAttendanceViewModel
            {
                UnitId = x.UnitId,
                UnitLabel = x.Unit?.Block is null ? string.Empty : $"{x.Unit.Block.Name} / {x.Unit.Number}",
                ResidentName = x.Resident?.Name ?? string.Empty,
                JoinedAt = x.JoinedAt
            }).ToList()
        };
    }

    private static AssemblyAgendaItemViewModel ToAgenda(
        AssemblyAgendaItemDTO item,
        CondominiumAssemblyDTO assembly,
        Guid? residentId,
        bool resultsVisible,
        decimal eligibleWeight)
    {
        var residentVote = residentId.HasValue
            ? item.Votes.FirstOrDefault(x => x.ResidentId == residentId.Value)
            : null;
        var result = Calculate(item, eligibleWeight);
        return new AssemblyAgendaItemViewModel
        {
            Id = item.Id, Order = item.Order, Title = item.Title, Description = item.Description,
            QuorumPercentage = item.QuorumPercentage, ApprovalPercentage = item.ApprovalPercentage,
            AbstentionCountsForQuorum = item.AbstentionCountsForQuorum,
            ParticipationWeight = resultsVisible ? result.ParticipationWeight : 0,
            ParticipationPercentage = resultsVisible ? result.ParticipationPercentage : 0,
            QuorumMet = resultsVisible && result.QuorumMet,
            ApprovalMet = resultsVisible && result.ApprovalMet,
            SelectedOptionId = residentVote?.OptionId, SelectedUnitId = residentVote?.UnitId,
            VoteRevision = residentVote?.Revision ?? 0,
            ResidentVotes = residentId.HasValue
                ? item.Votes.Where(x => x.ResidentId == residentId.Value)
                    .Select(x => new AssemblyResidentVoteViewModel { UnitId = x.UnitId, OptionId = x.OptionId, Revision = x.Revision }).ToList()
                : [],
            NamedVotes = resultsVisible && assembly.VoteVisibility == AssemblyVoteVisibilityEnum.Open
                ? item.Votes.OrderBy(x => x.Unit.Block.Name).ThenBy(x => x.Unit.Number)
                    .Select(x => new AssemblyNamedVoteViewModel
                    {
                        UnitLabel = $"{x.Unit.Block.Name} / {x.Unit.Number}",
                        ResidentName = x.Resident.Name,
                        OptionLabel = item.Options.First(o => o.Id == x.OptionId).Label,
                        CastAt = x.CastAt, Revision = x.Revision
                    }).ToList()
                : [],
            Options = item.Options.OrderBy(x => x.Order).Select(option =>
            {
                var optionVotes = item.Votes.Where(x => x.OptionId == option.Id).ToList();
                var weight = optionVotes.Sum(x => x.Weight);
                return new AssemblyVoteOptionViewModel
                {
                    Id = option.Id, Order = option.Order, Label = option.Label,
                    IsApproval = option.IsApproval, IsAbstention = option.IsAbstention,
                    VoteCount = resultsVisible ? optionVotes.Count : 0,
                    VoteWeight = resultsVisible ? weight : 0,
                    Percentage = resultsVisible && result.TotalCastWeight > 0
                        ? Math.Round(weight * 100m / result.TotalCastWeight, 2) : 0
                };
            }).ToList()
        };
    }

    internal static object ResultSummary(AssemblyAgendaItemDTO item, CondominiumAssemblyDTO assembly)
    {
        var result = Calculate(item, assembly.EligibleUnits.Where(x => x.IsEligible).Sum(x => x.Weight));
        return new { item.Id, result.ParticipationPercentage, result.QuorumMet, result.ApprovalMet };
    }

    internal static AssemblyResult Calculate(AssemblyAgendaItemDTO item, decimal eligibleWeight)
    {
        var totalCast = item.Votes.Sum(x => x.Weight);
        var abstentionIds = item.Options.Where(x => x.IsAbstention).Select(x => x.Id).ToHashSet();
        var participation = item.AbstentionCountsForQuorum
            ? totalCast
            : item.Votes.Where(x => !abstentionIds.Contains(x.OptionId)).Sum(x => x.Weight);
        var decisive = item.Votes.Where(x => !abstentionIds.Contains(x.OptionId)).Sum(x => x.Weight);
        var approvalIds = item.Options.Where(x => x.IsApproval).Select(x => x.Id).ToHashSet();
        var approval = item.Votes.Where(x => approvalIds.Contains(x.OptionId)).Sum(x => x.Weight);
        var participationPercentage = eligibleWeight > 0 ? Math.Round(participation * 100m / eligibleWeight, 2) : 0;
        var approvalPercentage = decisive > 0 ? approval * 100m / decisive : 0;
        var quorumMet = participationPercentage >= item.QuorumPercentage;
        return new AssemblyResult(totalCast, participation, participationPercentage, quorumMet,
            quorumMet && approvalPercentage >= item.ApprovalPercentage);
    }

    internal sealed record AssemblyResult(
        decimal TotalCastWeight,
        decimal ParticipationWeight,
        decimal ParticipationPercentage,
        bool QuorumMet,
        bool ApprovalMet);
}

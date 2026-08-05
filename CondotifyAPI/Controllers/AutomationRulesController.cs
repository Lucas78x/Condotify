using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/automation-rules")]
public sealed class AutomationRulesController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IAutomationRuleEvaluationService evaluator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ViewAutomations, HttpContext.RequestAborted)) return Forbid();
        var rules = await context.AutomationRules.AsNoTracking()
            .Include(x => x.Executions)
            .Where(x => x.LicenseId == licenseId).OrderBy(x => x.Name)
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rules.Select(ToOut));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid licenseId, [FromBody] AutomationRuleFormViewModel input)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageAutomations, HttpContext.RequestAborted)) return Forbid();
        var validation = Validate(input);
        if (validation is not null) return BadRequest(new { Errors = validation });
        if (await context.AutomationRules.AnyAsync(x => x.LicenseId == licenseId && x.Name.ToLower() == input.Name.Trim().ToLower(), HttpContext.RequestAborted))
            return Conflict(new { Errors = "Já existe uma regra com este nome." });
        var now = DateTime.UtcNow;
        var rule = new AutomationRuleDTO { Id = Guid.NewGuid(), LicenseId = licenseId, CreatedAt = now, CreatedByUserId = CurrentUserId(), CreatedByName = CurrentActor() };
        Apply(rule, input, now);
        context.AutomationRules.Add(rule);
        AddAudit(rule, "Created");
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(rule));
    }

    [HttpPut("{ruleId:guid}")]
    public async Task<IActionResult> Update(Guid licenseId, Guid ruleId, [FromBody] AutomationRuleFormViewModel input)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageAutomations, HttpContext.RequestAborted)) return Forbid();
        var validation = Validate(input);
        if (validation is not null) return BadRequest(new { Errors = validation });
        var rule = await context.AutomationRules.Include(x => x.Executions)
            .FirstOrDefaultAsync(x => x.Id == ruleId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (rule is null) return NotFound();
        if (await context.AutomationRules.AnyAsync(x => x.LicenseId == licenseId && x.Id != ruleId && x.Name.ToLower() == input.Name.Trim().ToLower(), HttpContext.RequestAborted))
            return Conflict(new { Errors = "Já existe uma regra com este nome." });
        Apply(rule, input, DateTime.UtcNow);
        AddAudit(rule, "Updated");
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(rule));
    }

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Delete(Guid licenseId, Guid ruleId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageAutomations, HttpContext.RequestAborted)) return Forbid();
        var rule = await context.AutomationRules.FirstOrDefaultAsync(x => x.Id == ruleId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (rule is null) return NotFound();
        AddAudit(rule, "Deleted");
        context.AutomationRules.Remove(rule);
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("{ruleId:guid}/evaluate")]
    public async Task<IActionResult> Evaluate(Guid licenseId, Guid ruleId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageAutomations, HttpContext.RequestAborted)) return Forbid();
        if (!await context.AutomationRules.AsNoTracking().AnyAsync(x => x.Id == ruleId && x.LicenseId == licenseId, HttpContext.RequestAborted)) return NotFound();
        var triggered = await evaluator.EvaluateAsync(ruleId, HttpContext.RequestAborted);
        return Ok(new { Triggered = triggered });
    }

    private static string? Validate(AutomationRuleFormViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Informe o nome da regra.";
        if (!Enum.IsDefined(typeof(AutomationTriggerTypeEnum), input.TriggerType) || !Enum.IsDefined(typeof(IncidentSeverityEnum), input.Severity)) return "Gatilho ou gravidade inválida.";
        if (input.Threshold is < 1 or > 10000 || input.WindowMinutes is < 1 or > 10080 || input.CooldownMinutes is < 5 or > 43200) return "Revise limite, janela e tempo de espera.";
        var actions = (AutomationActionEnum)input.Actions;
        return actions == AutomationActionEnum.None || (actions & ~(AutomationActionEnum.CreateIncident | AutomationActionEnum.CreateAlert)) != 0 ? "Selecione ao menos uma ação válida." : null;
    }

    private static void Apply(AutomationRuleDTO rule, AutomationRuleFormViewModel input, DateTime now)
    {
        rule.Name = input.Name.Trim(); rule.Description = input.Description.Trim();
        rule.TriggerType = (AutomationTriggerTypeEnum)input.TriggerType; rule.Threshold = input.Threshold;
        rule.WindowMinutes = input.WindowMinutes; rule.Severity = (IncidentSeverityEnum)input.Severity;
        rule.Actions = (AutomationActionEnum)input.Actions; rule.IsEnabled = input.IsEnabled;
        rule.CooldownMinutes = input.CooldownMinutes; rule.UpdatedAt = now;
    }

    private void AddAudit(AutomationRuleDTO rule, string action) => context.AccessOperationAudits.Add(new AccessOperationAuditDTO
    {
        Id = Guid.NewGuid(), LicenseId = rule.LicenseId, EntityType = "AutomationRule", EntityId = rule.Id,
        Action = action, Status = "Success", Summary = $"Regra {rule.Name}: {AuditActionLabel(action).ToLowerInvariant()}",
        DetailsJson = JsonSerializer.Serialize(new { rule.TriggerType, rule.Actions, rule.IsEnabled }),
        UserId = CurrentUserId(), UserName = CurrentActor(), CreatedAt = DateTime.UtcNow
    });

    private static AutomationRuleViewModel ToOut(AutomationRuleDTO x) => new()
    {
        Id = x.Id, LicenseId = x.LicenseId, Name = x.Name, Description = x.Description,
        TriggerType = x.TriggerType.ToString(), Threshold = x.Threshold, WindowMinutes = x.WindowMinutes,
        Severity = x.Severity.ToString(), Actions = (int)x.Actions, IsEnabled = x.IsEnabled,
        CooldownMinutes = x.CooldownMinutes, LastEvaluatedAt = x.LastEvaluatedAt,
        LastTriggeredAt = x.LastTriggeredAt, CreatedAt = x.CreatedAt, ExecutionCount = x.Executions.Count,
        RecentExecutions = x.Executions.OrderByDescending(e => e.CreatedAt).Take(8).Select(e => new AutomationExecutionViewModel
        { Id = e.Id, Status = e.Status, Summary = e.Summary, IncidentId = e.IncidentId, CreatedAt = e.CreatedAt }).ToList()
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentActor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuário";
    private static string AuditActionLabel(string action) => action switch
    {
        "Created" => "Criada",
        "Updated" => "Atualizada",
        "Deleted" => "Excluída",
        _ => "Alterada"
    };
}

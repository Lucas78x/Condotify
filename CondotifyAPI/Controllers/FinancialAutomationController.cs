using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Finance;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApiMobileNotificationCategory = CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/financial/automation")]
public sealed class FinancialAutomationController(
    DatabaseContext context,
    FinancialChargeImportCsvParser parser,
    IFinancialAutomationRunner runner,
    IFinancialReminderEmailSender email,
    IPlatformPushNotifier push) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewFinance)]
    public async Task<IActionResult> Get(Guid licenseId, CancellationToken cancellationToken)
    {
        var totalUnits = await context.Units.AsNoTracking().CountAsync(x => x.Block.LicenseId == licenseId, cancellationToken);
        var rules = await context.FinancialRecurringRules.AsNoTracking().Include(x => x.Units)
            .Where(x => x.LicenseId == licenseId).OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var policy = await context.FinancialReminderPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        var smtpPolicy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        var imports = await context.FinancialImportBatches.AsNoTracking().Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(cancellationToken);
        var deliveries = await context.FinancialReminderDeliveries.AsNoTracking()
            .Include(x => x.Charge).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Where(x => x.LicenseId == licenseId).OrderByDescending(x => x.CreatedAt).Take(80)
            .ToListAsync(cancellationToken);

        return Ok(new FinancialAutomationViewModel
        {
            Rules = rules.Select(x => ToRule(x, totalUnits)).ToList(),
            ReminderPolicy = ToPolicy(policy, email.IsReady(smtpPolicy)),
            Imports = imports.Select(ToImport).ToList(),
            RecentDeliveries = deliveries.Select(ToDelivery).ToList()
        });
    }

    [HttpPost("rules")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public Task<IActionResult> CreateRule(Guid licenseId, [FromBody] UpsertFinancialRecurringRuleViewModel input, CancellationToken cancellationToken) =>
        SaveRuleAsync(licenseId, null, input, cancellationToken);

    [HttpPut("rules/{ruleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public Task<IActionResult> UpdateRule(Guid licenseId, Guid ruleId, [FromBody] UpsertFinancialRecurringRuleViewModel input, CancellationToken cancellationToken) =>
        SaveRuleAsync(licenseId, ruleId, input, cancellationToken);

    [HttpPut("reminder-policy")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> UpdateReminderPolicy(Guid licenseId, [FromBody] UpdateFinancialReminderPolicyViewModel input, CancellationToken cancellationToken)
    {
        var before = input.BeforeDueDays.Distinct().OrderByDescending(x => x).ToList();
        if (before.Count > 10 || before.Any(x => x is < 1 or > 60))
            return BadRequest(new { Errors = "Informe no máximo 10 antecedências entre 1 e 60 dias." });
        if (input.Enabled && !input.PushEnabled && !input.EmailEnabled)
            return BadRequest(new { Errors = "Ative ao menos um canal para habilitar a régua." });
        if (input.MaxOverdueDays < input.FirstOverdueDay)
            return BadRequest(new { Errors = "O limite de atraso deve ser maior ou igual ao primeiro lembrete vencido." });
        var smtpPolicy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        if (input.Enabled && input.EmailEnabled && !email.IsReady(smtpPolicy))
            return Conflict(new { Errors = "Configure o SMTP do condomínio antes de ativar lembretes por e-mail." });

        var actor = ActorName();
        var policy = await context.FinancialReminderPolicies.FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        if (policy is null)
        {
            policy = new FinancialReminderPolicyDTO { LicenseId = licenseId };
            context.FinancialReminderPolicies.Add(policy);
        }
        policy.Enabled = input.Enabled;
        policy.PushEnabled = input.PushEnabled;
        policy.EmailEnabled = input.EmailEnabled;
        policy.BeforeDueDays = string.Join(',', before);
        policy.OnDueDate = input.OnDueDate;
        policy.FirstOverdueDay = input.FirstOverdueDay;
        policy.RepeatEveryDays = input.RepeatEveryDays;
        policy.MaxOverdueDays = input.MaxOverdueDays;
        policy.UpdatedBy = actor;
        policy.UpdatedAt = DateTime.UtcNow;
        AddAudit(licenseId, null, "FinancialReminderPolicyUpdated", "Success", "Régua de lembretes atualizada.", new
        {
            input.Enabled, input.PushEnabled, input.EmailEnabled, BeforeDueDays = before,
            input.OnDueDate, input.FirstOverdueDay, input.RepeatEveryDays, input.MaxOverdueDays
        });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToPolicy(policy, email.IsReady(smtpPolicy)));
    }

    [HttpPost("imports/preview")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> PreviewImport(Guid licenseId, [FromBody] FinancialImportRequestViewModel input, CancellationToken cancellationToken)
    {
        var requestError = ValidateImportRequest(input);
        if (requestError is not null) return BadRequest(new { Errors = requestError });
        return Ok(await BuildPreviewAsync(licenseId, input, cancellationToken));
    }

    [HttpPost("imports/execute")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> ExecuteImport(Guid licenseId, [FromBody] FinancialImportRequestViewModel input, CancellationToken cancellationToken)
    {
        var requestError = ValidateImportRequest(input);
        if (requestError is not null) return BadRequest(new { Errors = requestError });
        var existingBatch = await context.FinancialImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.IdempotencyKey == input.IdempotencyKey.Trim(), cancellationToken);
        if (existingBatch is not null)
            return Ok(new FinancialImportExecutionViewModel { Batch = ToImport(existingBatch), CreatedCharges = existingBatch.ImportedRows });

        var preview = await BuildPreviewAsync(licenseId, input, cancellationToken);
        if (!preview.CanExecute)
            return BadRequest(new { Errors = "Corrija todas as linhas antes de confirmar a importação.", Preview = preview });

        var actor = ActorName();
        var now = DateTime.UtcNow;
        var batch = new FinancialImportBatchDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, IdempotencyKey = input.IdempotencyKey.Trim(),
            FileName = Path.GetFileName(input.FileName), SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Content))).ToLowerInvariant(),
            Status = FinancialImportStatusEnum.Imported, TotalRows = preview.TotalRows, ImportedRows = preview.ValidRows,
            InvalidRows = preview.InvalidRows, TotalAmount = preview.TotalAmount, CreatedBy = actor, CreatedAt = now
        };
        context.FinancialImportBatches.Add(batch);
        var charges = new List<FinancialChargeDTO>();
        foreach (var row in preview.Rows)
        {
            var charge = new FinancialChargeDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, UnitId = row.UnitId!.Value,
                RequestKey = $"imp:{batch.Id:N}:{row.RowNumber:D4}", Competence = row.Competence,
                Reference = row.Reference.Trim(), Description = row.Description.Trim(), DueDate = row.DueDate!.Value,
                BaseAmount = row.BaseAmount, FineAmount = row.FineAmount, InterestAmount = row.InterestAmount,
                DiscountAmount = row.DiscountAmount, Status = FinancialChargeStatusEnum.Open, Notes = row.Notes.Trim(),
                CreatedBy = actor, UpdatedBy = actor, CreatedAt = now, UpdatedAt = now
            };
            charge.Events.Add(new FinancialChargeEventDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, ChargeId = charge.Id,
                EventType = "Imported", NewStatus = charge.Status, ActorType = "Equipe", ActorName = actor,
                Note = $"Importada do arquivo {batch.FileName}, linha {row.RowNumber}.", CreatedAt = now
            });
            context.FinancialCharges.Add(charge);
            charges.Add(charge);
        }
        AddAudit(licenseId, batch.Id, "FinancialChargesImported", "Success",
            $"{charges.Count} cobrança(s) importada(s).", new { batch.FileName, batch.SourceHash, batch.TotalRows, batch.TotalAmount });
        await context.SaveChangesAsync(cancellationToken);
        await NotifyImportedChargesAsync(charges, cancellationToken);
        return Ok(new FinancialImportExecutionViewModel { Batch = ToImport(batch), CreatedCharges = charges.Count });
    }

    [HttpPost("run")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> RunNow(Guid licenseId, CancellationToken cancellationToken)
    {
        var result = await runner.ProcessAsync(licenseId, cancellationToken);
        AddAudit(licenseId, null, "FinancialAutomationRun", "Success", "Automação financeira verificada manualmente.", result);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(result);
    }

    private async Task<IActionResult> SaveRuleAsync(Guid licenseId, Guid? ruleId, UpsertFinancialRecurringRuleViewModel input, CancellationToken cancellationToken)
    {
        var error = ValidateRule(input);
        if (error is not null) return BadRequest(new { Errors = error });
        var unitIds = input.UnitIds.Distinct().Take(501).ToList();
        if (!input.AllUnits && (unitIds.Count == 0 || unitIds.Count > 500))
            return BadRequest(new { Errors = "Selecione de 1 a 500 unidades ou marque todas as unidades." });
        if (!input.AllUnits)
        {
            var validUnits = await context.Units.AsNoTracking().CountAsync(x => unitIds.Contains(x.Id) && x.Block.LicenseId == licenseId, cancellationToken);
            if (validUnits != unitIds.Count) return BadRequest(new { Errors = "Uma ou mais unidades não pertencem a este condomínio." });
        }

        FinancialRecurringRuleDTO rule;
        var creating = !ruleId.HasValue;
        if (creating)
        {
            rule = new FinancialRecurringRuleDTO { Id = Guid.NewGuid(), LicenseId = licenseId, CreatedAt = DateTime.UtcNow, CreatedBy = ActorName() };
            context.FinancialRecurringRules.Add(rule);
        }
        else
        {
            var existingRule = await context.FinancialRecurringRules.Include(x => x.Units)
                .FirstOrDefaultAsync(x => x.Id == ruleId && x.LicenseId == licenseId, cancellationToken);
            if (existingRule is null) return NotFound();
            rule = existingRule;
        }

        var startMonth = ParseMonth(input.StartMonth);
        var endMonth = string.IsNullOrWhiteSpace(input.EndMonth) ? (DateTime?)null : ParseMonth(input.EndMonth);
        var actor = ActorName();
        rule.Name = input.Name.Trim();
        rule.AllUnits = input.AllUnits;
        rule.GenerationDay = input.GenerationDay;
        rule.DueDay = input.DueDay;
        rule.StartMonth = startMonth;
        rule.EndMonth = endMonth;
        rule.ReferenceTemplate = input.ReferenceTemplate.Trim();
        rule.Description = input.Description.Trim();
        rule.BaseAmount = Money(input.BaseAmount);
        rule.FineAmount = Money(input.FineAmount);
        rule.InterestAmount = Money(input.InterestAmount);
        rule.DiscountAmount = Money(input.DiscountAmount);
        rule.Notes = (input.Notes ?? string.Empty).Trim();
        rule.IsActive = input.IsActive;
        rule.UpdatedBy = actor;
        rule.UpdatedAt = DateTime.UtcNow;
        var currentMonth = ParseMonth(DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var nextMonth = startMonth > currentMonth ? startMonth : currentMonth;
        if (rule.LastRunAt.HasValue)
        {
            var afterLastRun = ParseMonth(rule.LastRunAt.Value.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture));
            if (afterLastRun > nextMonth) nextMonth = afterLastRun;
        }
        rule.NextRunAt = FinancialAutomationRunner.RunDate(nextMonth, rule.GenerationDay);
        var selectedUnitIds = input.AllUnits ? new HashSet<Guid>() : unitIds.ToHashSet();
        var linkedUnitIds = rule.Units.Select(x => x.UnitId).ToHashSet();
        context.FinancialRecurringRuleUnits.RemoveRange(rule.Units.Where(x => !selectedUnitIds.Contains(x.UnitId)));
        if (!input.AllUnits)
            foreach (var unitId in selectedUnitIds.Where(x => !linkedUnitIds.Contains(x)))
                rule.Units.Add(new FinancialRecurringRuleUnitDTO { RuleId = rule.Id, UnitId = unitId, LicenseId = licenseId });

        AddAudit(licenseId, rule.Id, creating ? "FinancialRecurringRuleCreated" : "FinancialRecurringRuleUpdated", "Success",
            creating ? "Regra recorrente criada." : "Regra recorrente atualizada.", new
            {
                rule.Name, rule.AllUnits, UnitCount = input.AllUnits ? (int?)null : unitIds.Count,
                rule.GenerationDay, rule.DueDay, input.StartMonth, input.EndMonth, rule.IsActive
            });
        await context.SaveChangesAsync(cancellationToken);
        var totalUnits = await context.Units.AsNoTracking().CountAsync(x => x.Block.LicenseId == licenseId, cancellationToken);
        return Ok(ToRule(rule, totalUnits));
    }

    private async Task<FinancialImportPreviewViewModel> BuildPreviewAsync(Guid licenseId, FinancialImportRequestViewModel input, CancellationToken cancellationToken)
    {
        var units = await context.Units.AsNoTracking().Where(x => x.Block.LicenseId == licenseId)
            .Select(x => new FinancialImportUnit(x.Id, x.Block.Name, x.Number, (x.Block.Name + " / " + x.Number).Trim()))
            .ToListAsync(cancellationToken);
        var existingRows = await context.FinancialCharges.AsNoTracking().Where(x => x.LicenseId == licenseId && x.Status != FinancialChargeStatusEnum.Cancelled)
            .Select(x => new { x.UnitId, x.Competence, x.Reference }).ToListAsync(cancellationToken);
        var existing = existingRows.Select(x => FinancialChargeImportCsvParser.ChargeKey(x.UnitId, x.Competence, x.Reference))
            .ToHashSet(StringComparer.Ordinal);
        return parser.Parse(input.FileName, input.Content, units, existing);
    }

    private async Task NotifyImportedChargesAsync(IReadOnlyCollection<FinancialChargeDTO> charges, CancellationToken cancellationToken)
    {
        if (charges.Count == 0) return;
        var unitIds = charges.Select(x => x.UnitId).Distinct().ToList();
        var now = DateTime.UtcNow;
        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => unitIds.Contains(x.UnitId) && x.IsActive && x.Resident.IsActive && x.StartsAt <= now && (!x.EndsAt.HasValue || x.EndsAt > now))
            .Select(x => new { x.UnitId, x.ResidentId }).ToListAsync(cancellationToken);
        foreach (var charge in charges)
            foreach (var residentId in links.Where(x => x.UnitId == charge.UnitId).Select(x => x.ResidentId).Distinct())
                await push.NotifyResidentAsync(residentId, ApiMobileNotificationCategory.Financial, "Nova cobrança disponível",
                    $"{charge.Reference} foi registrada para sua unidade. Consulte os detalhes no aplicativo.",
                    "/financeiro", $"financial-import:{charge.Id:N}", cancellationToken);
    }

    private static string? ValidateRule(UpsertFinancialRecurringRuleViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100) return "Informe um nome de até 100 caracteres.";
        if (input.GenerationDay is < 1 or > 28 || input.DueDay is < 1 or > 28) return "Os dias de geração e vencimento devem ficar entre 1 e 28.";
        if (!TryMonth(input.StartMonth, out var start)) return "Informe o mês inicial no formato AAAA-MM.";
        if (!string.IsNullOrWhiteSpace(input.EndMonth) && (!TryMonth(input.EndMonth, out var end) || end < start)) return "O mês final deve ser posterior ao mês inicial.";
        if (string.IsNullOrWhiteSpace(input.ReferenceTemplate) || input.ReferenceTemplate.Trim().Length > 80) return "Informe uma referência de até 80 caracteres.";
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 200) return "Informe uma descrição de até 200 caracteres.";
        if (input.BaseAmount <= 0 || input.FineAmount < 0 || input.InterestAmount < 0 || input.DiscountAmount < 0) return "Os valores informados são inválidos.";
        if (FinancialChargeCalculator.Total(input.BaseAmount, input.FineAmount, input.InterestAmount, input.DiscountAmount) <= 0) return "O total gerencial deve ser maior que zero.";
        return null;
    }

    private static string? ValidateImportRequest(FinancialImportRequestViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.FileName) || !string.Equals(Path.GetExtension(input.FileName), ".csv", StringComparison.OrdinalIgnoreCase)) return "Selecione uma planilha CSV.";
        if (string.IsNullOrWhiteSpace(input.Content)) return "O arquivo está vazio.";
        if (Encoding.UTF8.GetByteCount(input.Content) > 2_000_000) return "O arquivo excede o limite de 2 MB.";
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Trim().Length > 64) return "A chave da importação é inválida.";
        return null;
    }

    private static FinancialRecurringRuleViewModel ToRule(FinancialRecurringRuleDTO value, int totalUnits) => new()
    {
        Id = value.Id, Name = value.Name, AllUnits = value.AllUnits, UnitIds = value.Units.Select(x => x.UnitId).ToList(),
        UnitCount = value.AllUnits ? totalUnits : value.Units.Count, GenerationDay = value.GenerationDay, DueDay = value.DueDay,
        StartMonth = value.StartMonth.ToString("yyyy-MM"), EndMonth = value.EndMonth?.ToString("yyyy-MM") ?? string.Empty,
        ReferenceTemplate = value.ReferenceTemplate, Description = value.Description, BaseAmount = value.BaseAmount,
        FineAmount = value.FineAmount, InterestAmount = value.InterestAmount, DiscountAmount = value.DiscountAmount,
        Notes = value.Notes, IsActive = value.IsActive, NextRunAt = value.NextRunAt, LastRunAt = value.LastRunAt,
        LastGeneratedCount = value.LastGeneratedCount, UpdatedBy = value.UpdatedBy, UpdatedAt = value.UpdatedAt
    };

    private static FinancialReminderPolicyViewModel ToPolicy(FinancialReminderPolicyDTO? value, bool smtpReady) => new()
    {
        Enabled = value?.Enabled ?? false, PushEnabled = value?.PushEnabled ?? true, EmailEnabled = value?.EmailEnabled ?? false,
        BeforeDueDays = FinancialAutomationRunner.ParseBeforeDays(value?.BeforeDueDays ?? "5,1"), OnDueDate = value?.OnDueDate ?? true,
        FirstOverdueDay = value?.FirstOverdueDay ?? 1, RepeatEveryDays = value?.RepeatEveryDays ?? 7,
        MaxOverdueDays = value?.MaxOverdueDays ?? 90, EmailTransportReady = smtpReady,
        UpdatedBy = value?.UpdatedBy ?? string.Empty, UpdatedAt = value?.UpdatedAt ?? default
    };

    private static FinancialImportBatchViewModel ToImport(FinancialImportBatchDTO value) => new()
    {
        Id = value.Id, FileName = value.FileName, Status = (FinancialImportStatus)(int)value.Status,
        TotalRows = value.TotalRows, ImportedRows = value.ImportedRows, InvalidRows = value.InvalidRows,
        TotalAmount = value.TotalAmount, CreatedBy = value.CreatedBy, CreatedAt = value.CreatedAt
    };

    private static FinancialReminderDeliveryViewModel ToDelivery(FinancialReminderDeliveryDTO value) => new()
    {
        Id = value.Id, ChargeId = value.ChargeId, UnitLabel = (value.Charge.Unit.Block.Name + " / " + value.Charge.Unit.Number).Trim(),
        Reference = value.Charge.Reference, ResidentName = value.Resident.Name,
        Channel = (FinancialReminderChannel)(int)value.Channel, Status = (FinancialReminderDeliveryStatus)(int)value.Status,
        StageLabel = FinancialAutomationRunner.StageLabel(value.StageKey), DestinationLabel = value.DestinationLabel,
        AttemptCount = value.AttemptCount, LastError = value.LastError, CreatedAt = value.CreatedAt, FinishedAt = value.FinishedAt
    };

    private string ActorName() => User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administração";

    private void AddAudit(Guid licenseId, Guid? entityId, string action, string status, string summary, object details) =>
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "FinancialAutomation", EntityId = entityId,
            Action = action, Status = status, Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
            UserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null,
            UserName = ActorName(), CreatedAt = DateTime.UtcNow
        });

    private static bool TryMonth(string value, out DateTime month) => DateTime.TryParseExact(value?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out month) && (month = DateTime.SpecifyKind(new DateTime(month.Year, month.Month, 1), DateTimeKind.Utc)) != default;
    private static DateTime ParseMonth(string value) => TryMonth(value, out var month) ? month : throw new FormatException("Mês inválido.");
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

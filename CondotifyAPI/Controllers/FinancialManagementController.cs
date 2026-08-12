using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/financial")]
public sealed class FinancialManagementController(DatabaseContext context, IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewFinance)]
    public async Task<IActionResult> GetOverview(
        Guid licenseId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] bool overdueOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var now = DateTime.UtcNow;

        var all = await context.FinancialCharges.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .Select(x => new FinancialSummaryRow(
                x.UnitId, x.Status, x.DueDate, x.PaidAt,
                x.BaseAmount, x.FineAmount, x.InterestAmount, x.DiscountAmount))
            .ToListAsync(cancellationToken);

        var query = context.FinancialCharges.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(x => x.LicenseId == licenseId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x =>
                x.Reference.ToLower().Contains(term) ||
                x.Description.ToLower().Contains(term) ||
                x.Unit.Number.ToLower().Contains(term) ||
                x.Unit.Block.Name.ToLower().Contains(term));
        }

        if (Enum.TryParse<FinancialChargeStatusEnum>(status, true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);
        if (overdueOnly)
            query = query.Where(x => x.Status != FinancialChargeStatusEnum.Paid &&
                                     x.Status != FinancialChargeStatusEnum.Cancelled &&
                                     x.DueDate < now.Date);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Status == FinancialChargeStatusEnum.PaymentReported ? 0 : 1)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Unit.Block.Name)
            .ThenBy(x => x.Unit.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var units = await context.Units.AsNoTracking()
            .Where(x => x.Block.LicenseId == licenseId)
            .OrderBy(x => x.Block.Name).ThenBy(x => x.Number)
            .Select(x => new FinancialUnitOptionViewModel
            {
                Id = x.Id,
                Label = (x.Block.Name + " / " + x.Number).Trim()
            })
            .ToListAsync(cancellationToken);

        return Ok(new FinancialManagementViewModel
        {
            Summary = BuildSummary(all, now),
            Charges = items.Select(x => ToViewModel(x, now)).ToList(),
            Units = units,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("charges/{chargeId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ViewFinance)]
    public async Task<IActionResult> GetCharge(Guid licenseId, Guid chargeId, CancellationToken cancellationToken)
    {
        var charge = await context.FinancialCharges.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == chargeId && x.LicenseId == licenseId, cancellationToken);
        if (charge is null) return NotFound();

        var events = await context.FinancialChargeEvents.AsNoTracking()
            .Where(x => x.ChargeId == chargeId && x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FinancialChargeEventViewModel
            {
                Id = x.Id,
                EventType = x.EventType,
                PreviousStatus = x.PreviousStatus.HasValue ? (FinancialChargeStatus?)(int)x.PreviousStatus.Value : null,
                NewStatus = (FinancialChargeStatus)(int)x.NewStatus,
                ActorType = x.ActorType,
                ActorName = x.ActorName,
                Note = x.Note,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new FinancialChargeDetailViewModel { Charge = ToViewModel(charge, DateTime.UtcNow), Events = events });
    }

    [HttpPost("charges")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> CreateCharges(Guid licenseId, [FromBody] CreateFinancialChargesViewModel input, CancellationToken cancellationToken)
    {
        var error = ValidateChargeInput(input.UnitIds, input.Competence, input.Reference, input.Description,
            input.DueDate, input.BaseAmount, input.FineAmount, input.InterestAmount, input.DiscountAmount);
        if (error is not null) return BadRequest(new { Errors = error });
        if (input.RequestId == Guid.Empty) return BadRequest(new { Errors = "A chave da solicitação é obrigatória." });

        var requestedIds = input.UnitIds.Distinct().Take(501).ToList();
        if (requestedIds.Count > 500) return BadRequest(new { Errors = "Uma criação pode incluir no máximo 500 unidades." });

        var units = await context.Units.Include(x => x.Block)
            .Where(x => requestedIds.Contains(x.Id) && x.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        if (units.Count != requestedIds.Count)
            return BadRequest(new { Errors = "Uma ou mais unidades não pertencem a este condomínio." });

        var requestPrefix = input.RequestId.ToString("N");
        var keys = units.Select(x => $"{requestPrefix}:{x.Id:N}").ToList();
        var existing = await context.FinancialCharges
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(x => x.LicenseId == licenseId && keys.Contains(x.RequestKey))
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(x => x.RequestKey).ToHashSet(StringComparer.Ordinal);

        var dueDate = NormalizeDate(input.DueDate);
        var documents = await context.BoletoDocuments.AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.UnitId.HasValue && requestedIds.Contains(x.UnitId.Value) &&
                        x.Batch.LicenseId == licenseId && x.Batch.Status == BoletoBatchStatusEnum.Published &&
                        x.Batch.Reference == input.Reference.Trim() && x.Batch.DueDate.Date == dueDate.Date)
            .OrderByDescending(x => x.Batch.PublishedAt)
            .ToListAsync(cancellationToken);
        var documentByUnit = documents.GroupBy(x => x.UnitId!.Value).ToDictionary(x => x.Key, x => x.First().Id);

        var actor = Actor("Equipe");
        var now = DateTime.UtcNow;
        var created = new List<FinancialChargeDTO>();
        foreach (var unit in units)
        {
            var requestKey = $"{requestPrefix}:{unit.Id:N}";
            if (existingKeys.Contains(requestKey)) continue;
            var charge = new FinancialChargeDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, UnitId = unit.Id,
                BoletoDocumentId = documentByUnit.GetValueOrDefault(unit.Id), RequestKey = requestKey,
                Competence = input.Competence.Trim(), Reference = input.Reference.Trim(), Description = input.Description.Trim(),
                DueDate = dueDate, BaseAmount = Money(input.BaseAmount), FineAmount = Money(input.FineAmount),
                InterestAmount = Money(input.InterestAmount), DiscountAmount = Money(input.DiscountAmount),
                Status = FinancialChargeStatusEnum.Open, Notes = (input.Notes ?? string.Empty).Trim(),
                CreatedBy = actor.Name, UpdatedBy = actor.Name, CreatedAt = now, UpdatedAt = now,
                Unit = unit
            };
            charge.Events.Add(NewEvent(charge, "Created", null, charge.Status, actor, "Cobrança gerencial criada."));
            context.FinancialCharges.Add(charge);
            created.Add(charge);
        }

        AddAudit(licenseId, null, "FinancialChargesCreated", "Success",
            $"{created.Count} cobrança(s) gerencial(is) criada(s).", new { input.RequestId, UnitCount = created.Count, input.Competence });
        await context.SaveChangesAsync(cancellationToken);

        foreach (var charge in created)
            await NotifyResidentsAsync(charge, "Nova cobrança disponível",
                $"{charge.Reference} foi registrada para sua unidade. Consulte os detalhes no aplicativo.", cancellationToken);

        var result = existing.Concat(created).OrderBy(x => x.Unit.Block.Name).ThenBy(x => x.Unit.Number)
            .Select(x => ToViewModel(x, now)).ToList();
        return Created(string.Empty, result);
    }

    [HttpPut("charges/{chargeId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> UpdateCharge(Guid licenseId, Guid chargeId, [FromBody] UpdateFinancialChargeViewModel input, CancellationToken cancellationToken)
    {
        var error = ValidateChargeInput([Guid.NewGuid()], input.Competence, input.Reference, input.Description,
            input.DueDate, input.BaseAmount, input.FineAmount, input.InterestAmount, input.DiscountAmount);
        if (error is not null) return BadRequest(new { Errors = error });

        var charge = await context.FinancialCharges.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == chargeId && x.LicenseId == licenseId, cancellationToken);
        if (charge is null) return NotFound();
        if (charge.Status is FinancialChargeStatusEnum.Paid or FinancialChargeStatusEnum.Cancelled)
            return Conflict(new { Errors = "Reabra a cobrança antes de alterar seus valores." });

        var actor = Actor("Equipe");
        charge.Competence = input.Competence.Trim();
        charge.Reference = input.Reference.Trim();
        charge.Description = input.Description.Trim();
        charge.DueDate = NormalizeDate(input.DueDate);
        charge.BaseAmount = Money(input.BaseAmount);
        charge.FineAmount = Money(input.FineAmount);
        charge.InterestAmount = Money(input.InterestAmount);
        charge.DiscountAmount = Money(input.DiscountAmount);
        charge.Notes = (input.Notes ?? string.Empty).Trim();
        charge.UpdatedBy = actor.Name;
        charge.UpdatedAt = DateTime.UtcNow;
        charge.Events.Add(NewEvent(charge, "Updated", charge.Status, charge.Status, actor, "Dados da cobrança atualizados."));
        AddAudit(licenseId, charge.Id, "FinancialChargeUpdated", "Success", "Cobrança gerencial atualizada.", new { charge.UnitId, charge.Status });
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToViewModel(charge, DateTime.UtcNow));
    }

    [HttpPost("charges/{chargeId:guid}/action")]
    [RequireLicensePermission(LicensePermissionEnum.ManageFinance)]
    public async Task<IActionResult> ApplyAction(Guid licenseId, Guid chargeId, [FromBody] FinancialChargeActionViewModel input, CancellationToken cancellationToken)
    {
        if (input.PaidAt.HasValue && input.PaidAt.Value.Date > DateTime.UtcNow.Date.AddDays(1))
            return BadRequest(new { Errors = "A data informada do pagamento não pode estar no futuro." });
        var charge = await context.FinancialCharges.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == chargeId && x.LicenseId == licenseId, cancellationToken);
        if (charge is null) return NotFound();

        var previous = charge.Status;
        var transition = ResolveAction(previous, input);
        if (!transition.Success) return Conflict(new { Errors = transition.Error });

        var actor = Actor("Equipe");
        charge.Status = transition.Status;
        charge.PaidAt = transition.PaidAt;
        charge.PaymentReference = transition.PaymentReference;
        charge.UpdatedAt = DateTime.UtcNow;
        charge.UpdatedBy = actor.Name;
        charge.Events.Add(NewEvent(charge, input.Action.ToString(), previous, charge.Status, actor, (input.Note ?? string.Empty).Trim()));
        AddAudit(licenseId, charge.Id, $"Financial{input.Action}", "Success", "Situação financeira gerencial atualizada.",
            new { charge.UnitId, Previous = previous.ToString(), Current = charge.Status.ToString() });
        await context.SaveChangesAsync(cancellationToken);

        await NotifyResidentsAsync(charge, "Situação financeira atualizada",
            $"A situação de {charge.Reference} foi atualizada pela administração.", cancellationToken);
        return Ok(ToViewModel(charge, DateTime.UtcNow));
    }

    internal static FinancialSummaryViewModel BuildSummary(IEnumerable<FinancialSummaryRow> rows, DateTime now)
    {
        var list = rows.ToList();
        var open = list.Where(x => IsCollectable(x.Status)).ToList();
        var overdue = open.Where(x => x.DueDate.Date < now.Date).ToList();
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = monthStart.AddMonths(1);

        return new FinancialSummaryViewModel
        {
            OpenAmount = open.Sum(Total),
            OverdueAmount = overdue.Sum(Total),
            PaidThisMonthAmount = list.Where(x => x.Status == FinancialChargeStatusEnum.Paid && x.PaidAt >= monthStart && x.PaidAt < nextMonth).Sum(Total),
            OpenCharges = open.Count,
            OverdueCharges = overdue.Count,
            DelinquentUnits = overdue.Select(x => x.UnitId).Distinct().Count(),
            PaymentReportsPending = list.Count(x => x.Status == FinancialChargeStatusEnum.PaymentReported),
            Aging =
            [
                Bucket("Até 30 dias", overdue.Where(x => Days(x.DueDate, now) <= 30)),
                Bucket("31 a 60 dias", overdue.Where(x => Days(x.DueDate, now) is >= 31 and <= 60)),
                Bucket("61 a 90 dias", overdue.Where(x => Days(x.DueDate, now) is >= 61 and <= 90)),
                Bucket("Mais de 90 dias", overdue.Where(x => Days(x.DueDate, now) > 90))
            ]
        };
    }

    internal static FinancialActionResolution ResolveAction(FinancialChargeStatusEnum current, FinancialChargeActionViewModel input)
    {
        var noteRequired = input.Action is FinancialChargeAction.RejectPaymentReport or FinancialChargeAction.MarkNegotiated or
            FinancialChargeAction.MarkDisputed or FinancialChargeAction.Cancel or FinancialChargeAction.Reopen;
        if (noteRequired && string.IsNullOrWhiteSpace(input.Note)) return FinancialActionResolution.Fail("Informe o motivo desta alteração.");

        return input.Action switch
        {
            FinancialChargeAction.ConfirmPayment when current is not FinancialChargeStatusEnum.Paid and not FinancialChargeStatusEnum.Cancelled =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Paid, NormalizeOptionalDate(input.PaidAt ?? DateTime.UtcNow), (input.PaymentReference ?? string.Empty).Trim()),
            FinancialChargeAction.RejectPaymentReport when current == FinancialChargeStatusEnum.PaymentReported =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Open),
            FinancialChargeAction.MarkNegotiated when current is FinancialChargeStatusEnum.Open or FinancialChargeStatusEnum.PaymentReported or FinancialChargeStatusEnum.Disputed =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Negotiated),
            FinancialChargeAction.MarkDisputed when current is not FinancialChargeStatusEnum.Paid and not FinancialChargeStatusEnum.Cancelled =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Disputed),
            FinancialChargeAction.Cancel when current is not FinancialChargeStatusEnum.Paid and not FinancialChargeStatusEnum.Cancelled =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Cancelled),
            FinancialChargeAction.Reopen when current != FinancialChargeStatusEnum.Open =>
                FinancialActionResolution.Ok(FinancialChargeStatusEnum.Open),
            _ => FinancialActionResolution.Fail("Esta alteração não é permitida para a situação atual.")
        };
    }

    internal static FinancialChargeViewModel ToViewModel(FinancialChargeDTO charge, DateTime now)
    {
        var status = (FinancialChargeStatus)(int)charge.Status;
        return new FinancialChargeViewModel
        {
            Id = charge.Id, UnitId = charge.UnitId,
            UnitLabel = (charge.Unit.Block.Name + " / " + charge.Unit.Number).Trim(),
            BoletoDocumentId = charge.BoletoDocumentId, Competence = charge.Competence,
            Reference = charge.Reference, Description = charge.Description, DueDate = charge.DueDate,
            BaseAmount = charge.BaseAmount, FineAmount = charge.FineAmount, InterestAmount = charge.InterestAmount,
            DiscountAmount = charge.DiscountAmount,
            TotalAmount = FinancialChargeCalculator.Total(charge.BaseAmount, charge.FineAmount, charge.InterestAmount, charge.DiscountAmount),
            Status = status, DisplayStatus = FinancialChargeCalculator.DisplayStatus(status, charge.DueDate, now),
            IsOverdue = FinancialChargeCalculator.IsOverdue(status, charge.DueDate, now),
            DaysOverdue = FinancialChargeCalculator.DaysOverdue(status, charge.DueDate, now),
            PaidAt = charge.PaidAt, PaymentReference = charge.PaymentReference, Notes = charge.Notes,
            CreatedBy = charge.CreatedBy, CreatedAt = charge.CreatedAt, UpdatedBy = charge.UpdatedBy, UpdatedAt = charge.UpdatedAt
        };
    }

    private async Task NotifyResidentsAsync(FinancialChargeDTO charge, string title, string body, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var residentIds = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.UnitId == charge.UnitId && x.IsActive && x.StartsAt <= now && (!x.EndsAt.HasValue || x.EndsAt > now))
            .Select(x => x.ResidentId).Distinct().ToListAsync(cancellationToken);
        foreach (var residentId in residentIds)
            await notifier.NotifyResidentAsync(residentId, CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory.Financial, title, body,
                "/financeiro", $"financial:{charge.Id:N}:{charge.UpdatedAt.Ticks}", cancellationToken);
    }

    private ActorInfo Actor(string type) => new(
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null,
        User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administração",
        type);

    private void AddAudit(Guid licenseId, Guid? entityId, string action, string status, string summary, object details) =>
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "FinancialCharge", EntityId = entityId,
            Action = action, Status = status, Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
            UserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null,
            UserName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administração", CreatedAt = DateTime.UtcNow
        });

    private static FinancialChargeEventDTO NewEvent(FinancialChargeDTO charge, string type, FinancialChargeStatusEnum? previous,
        FinancialChargeStatusEnum next, ActorInfo actor, string note) => new()
        {
            Id = Guid.NewGuid(), LicenseId = charge.LicenseId, ChargeId = charge.Id, EventType = type,
            PreviousStatus = previous, NewStatus = next, ActorType = actor.Type, ActorId = actor.Id,
            ActorName = actor.Name, Note = (note ?? string.Empty).Trim(), CreatedAt = DateTime.UtcNow
        };

    private static string? ValidateChargeInput(IReadOnlyCollection<Guid> unitIds, string competence, string reference,
        string description, DateTime dueDate, decimal baseAmount, decimal fineAmount, decimal interestAmount, decimal discountAmount)
    {
        if (unitIds.Count == 0) return "Selecione pelo menos uma unidade.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(competence?.Trim() ?? string.Empty, @"^\d{4}-(0[1-9]|1[0-2])$")) return "Informe a competência no formato AAAA-MM.";
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 80) return "Informe uma referência de até 80 caracteres.";
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 200) return "Informe uma descrição de até 200 caracteres.";
        if (dueDate == default) return "Informe o vencimento.";
        if (baseAmount <= 0 || fineAmount < 0 || interestAmount < 0 || discountAmount < 0) return "Os valores informados são inválidos.";
        if (FinancialChargeCalculator.Total(baseAmount, fineAmount, interestAmount, discountAmount) <= 0) return "O total da cobrança deve ser maior que zero.";
        return null;
    }

    private static DateTime NormalizeDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private static DateTime NormalizeOptionalDate(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static bool IsCollectable(FinancialChargeStatusEnum status) => status is not FinancialChargeStatusEnum.Paid and not FinancialChargeStatusEnum.Cancelled;
    private static decimal Total(FinancialSummaryRow x) => FinancialChargeCalculator.Total(x.BaseAmount, x.FineAmount, x.InterestAmount, x.DiscountAmount);
    private static int Days(DateTime dueDate, DateTime now) => Math.Max(0, (now.Date - dueDate.Date).Days);
    private static FinancialAgingBucketViewModel Bucket(string label, IEnumerable<FinancialSummaryRow> rows)
    {
        var list = rows.ToList();
        return new FinancialAgingBucketViewModel { Label = label, Count = list.Count, Amount = list.Sum(Total) };
    }

    internal sealed record FinancialSummaryRow(Guid UnitId, FinancialChargeStatusEnum Status, DateTime DueDate, DateTime? PaidAt,
        decimal BaseAmount, decimal FineAmount, decimal InterestAmount, decimal DiscountAmount);
    internal sealed record FinancialActionResolution(bool Success, FinancialChargeStatusEnum Status, DateTime? PaidAt, string PaymentReference, string Error)
    {
        public static FinancialActionResolution Ok(FinancialChargeStatusEnum status, DateTime? paidAt = null, string paymentReference = "") => new(true, status, paidAt, paymentReference, string.Empty);
        public static FinancialActionResolution Fail(string error) => new(false, default, null, string.Empty, error);
    }
    private sealed record ActorInfo(Guid? Id, string Name, string Type);
}

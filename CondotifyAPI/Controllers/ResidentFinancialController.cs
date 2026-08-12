using System.Security.Claims;
using System.Text.Json;
using System.Linq.Expressions;
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
[Authorize(Policy = "Resident")]
[Route("api/resident/financial")]
public sealed class ResidentFinancialController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (grant.UnitIds.Count == 0) return Ok(new ResidentFinancialOverviewViewModel());

        var now = DateTime.UtcNow;
        var charges = await context.FinancialCharges.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(IsVisibleTo(grant))
            .OrderBy(x => x.Status == FinancialChargeStatusEnum.PaymentReported ? 0 : 1)
            .ThenByDescending(x => x.DueDate)
            .Take(250)
            .ToListAsync(cancellationToken);
        var models = charges.Select(x => ToResidentViewModel(x, now)).ToList();
        var active = models.Where(x => x.Status is not FinancialChargeStatus.Paid and not FinancialChargeStatus.Cancelled).ToList();
        var reminders = await context.FinancialReminderDeliveries.AsNoTracking()
            .Where(x => x.LicenseId == grant.LicenseId && x.ResidentId == grant.ResidentId &&
                        x.Status == FinancialReminderDeliveryStatusEnum.Delivered && x.FinishedAt.HasValue &&
                        grant.UnitIds.Contains(x.Charge.UnitId))
            .OrderByDescending(x => x.FinishedAt)
            .Take(12)
            .Select(x => new ResidentFinancialReminderViewModel
            {
                ChargeId = x.ChargeId,
                Reference = x.Charge.Reference,
                StageLabel = x.StageKey == "due" ? "Vence hoje" :
                    x.StageKey.StartsWith("before-") ? "Lembrete antes do vencimento" : "Lembrete de vencimento",
                Channel = (FinancialReminderChannel)(int)x.Channel,
                SentAt = x.FinishedAt!.Value
            })
            .ToListAsync(cancellationToken);
        return Ok(new ResidentFinancialOverviewViewModel
        {
            OpenAmount = active.Sum(x => x.TotalAmount),
            OverdueAmount = active.Where(x => x.IsOverdue).Sum(x => x.TotalAmount),
            OverdueCharges = active.Count(x => x.IsOverdue),
            PendingAnalysis = models.Count(x => x.Status is FinancialChargeStatus.PaymentReported or FinancialChargeStatus.Disputed),
            Charges = models,
            RecentReminders = reminders
        });
    }

    [HttpPost("charges/{chargeId:guid}/payment-report")]
    public Task<IActionResult> ReportPayment(Guid chargeId, [FromBody] ResidentFinancialActionViewModel input, CancellationToken cancellationToken) =>
        ApplyResidentAction(chargeId, input, true, cancellationToken);

    [HttpPost("charges/{chargeId:guid}/dispute")]
    public Task<IActionResult> Dispute(Guid chargeId, [FromBody] ResidentFinancialActionViewModel input, CancellationToken cancellationToken) =>
        ApplyResidentAction(chargeId, input, false, cancellationToken);

    private async Task<IActionResult> ApplyResidentAction(Guid chargeId, ResidentFinancialActionViewModel input, bool paymentReport, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Note) || input.Note.Trim().Length is < 3 or > 500)
            return BadRequest(new { Errors = "Descreva a informação em 3 a 500 caracteres." });
        if (paymentReport && input.PaidAt.HasValue && input.PaidAt.Value.Date > DateTime.UtcNow.Date.AddDays(1))
            return BadRequest(new { Errors = "A data informada do pagamento não pode estar no futuro." });

        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        var charge = await context.FinancialCharges.Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(IsVisibleTo(grant))
            .FirstOrDefaultAsync(x => x.Id == chargeId, cancellationToken);
        if (charge is null) return NotFound();
        if (charge.Status is FinancialChargeStatusEnum.Paid or FinancialChargeStatusEnum.Cancelled)
            return Conflict(new { Errors = "Esta cobrança não aceita novas manifestações." });
        if (paymentReport && charge.Status == FinancialChargeStatusEnum.PaymentReported)
            return Ok(ToResidentViewModel(charge, DateTime.UtcNow));
        if (!paymentReport && charge.Status == FinancialChargeStatusEnum.Disputed)
            return Ok(ToResidentViewModel(charge, DateTime.UtcNow));

        var previous = charge.Status;
        charge.Status = paymentReport ? FinancialChargeStatusEnum.PaymentReported : FinancialChargeStatusEnum.Disputed;
        charge.PaidAt = paymentReport && input.PaidAt.HasValue ? DateTime.SpecifyKind(input.PaidAt.Value, DateTimeKind.Utc) : null;
        charge.UpdatedAt = DateTime.UtcNow;
        charge.UpdatedBy = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Morador";
        charge.Events.Add(new FinancialChargeEventDTO
        {
            Id = Guid.NewGuid(), LicenseId = charge.LicenseId, ChargeId = charge.Id,
            EventType = paymentReport ? "PaymentReported" : "Disputed",
            PreviousStatus = previous, NewStatus = charge.Status, ActorType = "Morador",
            ActorId = grant.ResidentId, ActorName = charge.UpdatedBy, Note = input.Note.Trim(), CreatedAt = DateTime.UtcNow
        });
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = charge.LicenseId, EntityType = "FinancialCharge", EntityId = charge.Id,
            Action = paymentReport ? "PaymentReported" : "Disputed", Status = "Recorded",
            Summary = paymentReport ? "Morador informou pagamento externo." : "Morador contestou cobrança gerencial.",
            DetailsJson = JsonSerializer.Serialize(new { charge.UnitId, Previous = previous.ToString(), Current = charge.Status.ToString() }),
            UserId = grant.ResidentId, UserName = charge.UpdatedBy, CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);

        await notifier.NotifyLicenseUsersAsync(charge.LicenseId, CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory.Financial,
            paymentReport ? "Pagamento informado" : "Cobrança contestada",
            $"Há uma nova manifestação financeira da unidade {(charge.Unit.Block.Name + " / " + charge.Unit.Number).Trim()}.",
            "/notifications", $"financial-resident-action:{charge.Id:N}:{charge.UpdatedAt.Ticks}", cancellationToken);
        return Ok(ToResidentViewModel(charge, DateTime.UtcNow));
    }

    internal static Expression<Func<FinancialChargeDTO, bool>> IsVisibleTo(ResidentAccessGrant grant) =>
        charge => charge.LicenseId == grant.LicenseId && grant.UnitIds.Contains(charge.UnitId);

    internal static FinancialChargeViewModel ToResidentViewModel(FinancialChargeDTO charge, DateTime now)
    {
        var model = FinancialManagementController.ToViewModel(charge, now);
        model.Notes = string.Empty;
        model.PaymentReference = string.Empty;
        model.CreatedBy = string.Empty;
        model.UpdatedBy = string.Empty;
        return model;
    }
}

using System.Globalization;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Mobile;
using Microsoft.EntityFrameworkCore;
using ApiMobileNotificationCategory = CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory;

namespace CondotifyAPI.Services.Finance;

public interface IFinancialAutomationRunner
{
    Task<FinancialAutomationRunViewModel> ProcessAsync(Guid? licenseId = null, CancellationToken cancellationToken = default);
}

public sealed class FinancialAutomationRunner(
    DatabaseContext context,
    IPlatformPushNotifier push,
    IFinancialReminderEmailSender email,
    ILogger<FinancialAutomationRunner> logger) : IFinancialAutomationRunner
{
    public async Task<FinancialAutomationRunViewModel> ProcessAsync(Guid? licenseId = null, CancellationToken cancellationToken = default)
    {
        var result = new FinancialAutomationRunViewModel();
        result.GeneratedCharges = await GenerateRecurringChargesAsync(licenseId, cancellationToken);
        result.ScheduledReminders = await ScheduleRemindersAsync(licenseId, cancellationToken);
        result.DeliveredReminders = await DispatchRemindersAsync(licenseId, cancellationToken);
        return result;
    }

    private async Task<int> GenerateRecurringChargesAsync(Guid? licenseId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = Today(now);
        var query = context.FinancialRecurringRules
            .Include(x => x.Units)
            .Where(x => x.IsActive && x.NextRunAt <= today.AddDays(1));
        if (licenseId.HasValue) query = query.Where(x => x.LicenseId == licenseId.Value);
        var rules = await query.OrderBy(x => x.NextRunAt).Take(50).ToListAsync(cancellationToken);
        var generated = 0;
        foreach (var rule in rules)
        {
            var loops = 0;
            var generatedForRule = 0;
            while (rule.IsActive && rule.NextRunAt.Date <= today && loops++ < 12)
            {
                var competence = Month(rule.NextRunAt);
                if (rule.EndMonth.HasValue && competence > Month(rule.EndMonth.Value))
                {
                    rule.IsActive = false;
                    break;
                }

                var unitsQuery = context.Units.Include(x => x.Block)
                    .Where(x => x.Block.LicenseId == rule.LicenseId);
                if (!rule.AllUnits)
                {
                    var selected = rule.Units.Select(x => x.UnitId).ToList();
                    unitsQuery = unitsQuery.Where(x => selected.Contains(x.Id));
                }
                var units = await unitsQuery.OrderBy(x => x.Block.Name).ThenBy(x => x.Number).Take(1000).ToListAsync(cancellationToken);
                var keys = units.Select(x => RecurringKey(rule.Id, competence, x.Id)).ToList();
                var existing = await context.FinancialCharges.AsNoTracking()
                    .Where(x => x.LicenseId == rule.LicenseId && keys.Contains(x.RequestKey))
                    .Select(x => x.RequestKey).ToHashSetAsync(cancellationToken);
                var created = new List<FinancialChargeDTO>();
                foreach (var unit in units)
                {
                    var key = RecurringKey(rule.Id, competence, unit.Id);
                    if (existing.Contains(key)) continue;
                    var reference = Expand(rule.ReferenceTemplate, competence, unit.Number, 80);
                    var description = Expand(rule.Description, competence, unit.Number, 200);
                    var charge = new FinancialChargeDTO
                    {
                        Id = Guid.NewGuid(), LicenseId = rule.LicenseId, UnitId = unit.Id, Unit = unit,
                        RequestKey = key, Competence = competence.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                        Reference = reference, Description = description,
                        DueDate = DateTime.SpecifyKind(new DateTime(competence.Year, competence.Month, rule.DueDay), DateTimeKind.Utc),
                        BaseAmount = Money(rule.BaseAmount), FineAmount = Money(rule.FineAmount),
                        InterestAmount = Money(rule.InterestAmount), DiscountAmount = Money(rule.DiscountAmount),
                        Status = FinancialChargeStatusEnum.Open, Notes = rule.Notes,
                        CreatedBy = "Automação financeira", UpdatedBy = "Automação financeira", CreatedAt = now, UpdatedAt = now
                    };
                    charge.Events.Add(new FinancialChargeEventDTO
                    {
                        Id = Guid.NewGuid(), LicenseId = charge.LicenseId, ChargeId = charge.Id,
                        EventType = "RecurringCreated", NewStatus = charge.Status, ActorType = "System",
                        ActorName = "Automação financeira", Note = $"Gerada pela regra {rule.Name}.", CreatedAt = now
                    });
                    context.FinancialCharges.Add(charge);
                    created.Add(charge);
                }

                generatedForRule += created.Count;
                generated += created.Count;
                rule.LastRunAt = now;
                rule.LastGeneratedCount = created.Count;
                rule.UpdatedBy = "Automação financeira";
                rule.UpdatedAt = now;
                rule.NextRunAt = RunDate(competence.AddMonths(1), rule.GenerationDay);
                await context.SaveChangesAsync(cancellationToken);

                foreach (var charge in created)
                    await NotifyNewChargeAsync(charge, cancellationToken);
            }
            if (loops >= 12)
                logger.LogWarning("Regra financeira {RuleId} atingiu o limite de competências por execução", rule.Id);
            if (generatedForRule == 0 && context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync(cancellationToken);
        }
        return generated;
    }

    private async Task<int> ScheduleRemindersAsync(Guid? licenseId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var today = Today(now);
        var policyQuery = context.FinancialReminderPolicies.AsNoTracking().Where(x => x.Enabled && (x.PushEnabled || x.EmailEnabled));
        if (licenseId.HasValue) policyQuery = policyQuery.Where(x => x.LicenseId == licenseId.Value);
        var policies = await policyQuery.ToDictionaryAsync(x => x.LicenseId, cancellationToken);
        if (policies.Count == 0) return 0;

        var licenseIds = policies.Keys.ToList();
        var maxBefore = policies.Values.SelectMany(x => ParseBeforeDays(x.BeforeDueDays)).DefaultIfEmpty(0).Max();
        var maxOverdue = policies.Values.Max(x => x.MaxOverdueDays);
        var charges = await context.FinancialCharges.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Where(x => licenseIds.Contains(x.LicenseId) &&
                        x.Status != FinancialChargeStatusEnum.Paid && x.Status != FinancialChargeStatusEnum.Cancelled &&
                        x.DueDate >= today.AddDays(-maxOverdue) && x.DueDate <= today.AddDays(maxBefore))
            .ToListAsync(cancellationToken);
        if (charges.Count == 0) return 0;

        var unitIds = charges.Select(x => x.UnitId).Distinct().ToList();
        var links = await context.ResidentUnitLinks.AsNoTracking().Include(x => x.Resident)
            .Where(x => unitIds.Contains(x.UnitId) && x.IsActive && x.Resident.IsActive &&
                        x.StartsAt <= now && (!x.EndsAt.HasValue || x.EndsAt > now))
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(x => x.Id).ToList();
        var existingKeys = await context.FinancialReminderDeliveries.AsNoTracking()
            .Where(x => chargeIds.Contains(x.ChargeId)).Select(x => x.DeliveryKey).ToHashSetAsync(cancellationToken);
        var scheduled = 0;
        foreach (var charge in charges)
        {
            var policy = policies[charge.LicenseId];
            var stage = ResolveStage(policy, charge.DueDate, today);
            if (stage is null) continue;
            foreach (var link in links.Where(x => x.UnitId == charge.UnitId).GroupBy(x => x.ResidentId).Select(x => x.First()))
            {
                if (policy.PushEnabled)
                    scheduled += AddDelivery(charge, link.ResidentId, FinancialReminderChannelEnum.Push, stage, "Aplicativo Condotify", existingKeys, now);
                if (policy.EmailEnabled && !string.IsNullOrWhiteSpace(link.Resident.Email))
                    scheduled += AddDelivery(charge, link.ResidentId, FinancialReminderChannelEnum.Email, stage,
                        FinancialReminderEmailSender.Mask(link.Resident.Email), existingKeys, now);
            }
        }
        if (scheduled > 0)
        {
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { context.ChangeTracker.Clear(); }
        }
        return scheduled;
    }

    private int AddDelivery(FinancialChargeDTO charge, Guid residentId, FinancialReminderChannelEnum channel,
        FinancialReminderStage stage, string destination, HashSet<string> existingKeys, DateTime now)
    {
        var key = $"financial-reminder:{charge.Id:N}:{residentId:N}:{(int)channel}:{stage.Key}";
        if (!existingKeys.Add(key)) return 0;
        context.FinancialReminderDeliveries.Add(new FinancialReminderDeliveryDTO
        {
            Id = Guid.NewGuid(), LicenseId = charge.LicenseId, ChargeId = charge.Id, ResidentId = residentId,
            Channel = channel, Status = FinancialReminderDeliveryStatusEnum.Queued,
            StageKey = stage.Key, DeliveryKey = key, DestinationLabel = destination, MaxAttempts = 5, CreatedAt = now
        });
        return 1;
    }

    private async Task<int> DispatchRemindersAsync(Guid? licenseId, CancellationToken cancellationToken)
    {
        var delivered = 0;
        for (var index = 0; index < 50; index++)
        {
            var delivery = await ClaimAsync(licenseId, cancellationToken);
            if (delivery is null) break;
            var now = DateTime.UtcNow;
            if (delivery.Charge.Status is FinancialChargeStatusEnum.Paid or FinancialChargeStatusEnum.Cancelled)
            {
                delivery.Status = FinancialReminderDeliveryStatusEnum.Cancelled;
                delivery.FinishedAt = now;
                await context.SaveChangesAsync(cancellationToken);
                continue;
            }
            var reminderPolicy = await context.FinancialReminderPolicies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LicenseId == delivery.LicenseId, cancellationToken);
            var channelEnabled = reminderPolicy?.Enabled == true &&
                (delivery.Channel == FinancialReminderChannelEnum.Push ? reminderPolicy.PushEnabled : reminderPolicy.EmailEnabled);
            if (!channelEnabled)
            {
                delivery.Status = FinancialReminderDeliveryStatusEnum.Cancelled;
                delivery.FinishedAt = now;
                await context.SaveChangesAsync(cancellationToken);
                continue;
            }

            var stageLabel = StageLabel(delivery.StageKey);
            var success = false;
            var error = string.Empty;
            if (delivery.Channel == FinancialReminderChannelEnum.Push)
            {
                await push.NotifyResidentAsync(delivery.ResidentId, ApiMobileNotificationCategory.Financial,
                    stageLabel, $"Há uma atualização sobre {delivery.Charge.Reference}. Consulte os detalhes no aplicativo.",
                    "/financeiro", delivery.DeliveryKey, cancellationToken);
                success = true;
            }
            else
            {
                var smtpPolicy = await context.AlertNotificationPolicies.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.LicenseId == delivery.LicenseId, cancellationToken);
                var send = await email.SendAsync(smtpPolicy, delivery.Resident.Email, delivery.Resident.Name,
                    delivery.Charge.License.Name, delivery.Charge.Reference, stageLabel, delivery.Charge.DueDate, cancellationToken);
                success = send.Success;
                error = send.Error;
            }

            delivery.LastError = Short(error, 1000);
            if (success)
            {
                delivery.Status = FinancialReminderDeliveryStatusEnum.Delivered;
                delivery.FinishedAt = now;
                delivered++;
            }
            else if (delivery.AttemptCount >= delivery.MaxAttempts)
            {
                delivery.Status = FinancialReminderDeliveryStatusEnum.DeadLetter;
                delivery.FinishedAt = now;
            }
            else
            {
                delivery.Status = FinancialReminderDeliveryStatusEnum.Failed;
                delivery.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, delivery.AttemptCount)));
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        return delivered;
    }

    private async Task<FinancialReminderDeliveryDTO?> ClaimAsync(Guid? licenseId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = context.FinancialReminderDeliveries.AsNoTracking()
            .Where(x => x.AttemptCount < x.MaxAttempts && (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                        (x.Status == FinancialReminderDeliveryStatusEnum.Queued || x.Status == FinancialReminderDeliveryStatusEnum.Failed));
        if (licenseId.HasValue) query = query.Where(x => x.LicenseId == licenseId.Value);
        var ids = await query.OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(8).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            var claimed = await context.FinancialReminderDeliveries
                .Where(x => x.Id == id && x.AttemptCount < x.MaxAttempts &&
                            (x.Status == FinancialReminderDeliveryStatusEnum.Queued || x.Status == FinancialReminderDeliveryStatusEnum.Failed))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, FinancialReminderDeliveryStatusEnum.Sending)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.SentAt, now), cancellationToken);
            if (claimed == 0) continue;
            context.ChangeTracker.Clear();
            return await context.FinancialReminderDeliveries
                .Include(x => x.Resident)
                .Include(x => x.Charge).ThenInclude(x => x.License)
                .FirstAsync(x => x.Id == id, cancellationToken);
        }
        return null;
    }

    private async Task NotifyNewChargeAsync(FinancialChargeDTO charge, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var residents = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.UnitId == charge.UnitId && x.IsActive && x.Resident.IsActive &&
                        x.StartsAt <= now && (!x.EndsAt.HasValue || x.EndsAt > now))
            .Select(x => x.ResidentId).Distinct().ToListAsync(cancellationToken);
        foreach (var residentId in residents)
            await push.NotifyResidentAsync(residentId, ApiMobileNotificationCategory.Financial, "Nova cobrança disponível",
                $"{charge.Reference} foi registrada para sua unidade. Consulte os detalhes no aplicativo.",
                "/financeiro", $"financial-recurring:{charge.Id:N}", cancellationToken);
    }

    internal static FinancialReminderStage? ResolveStage(FinancialReminderPolicyDTO policy, DateTime dueDate, DateTime today)
    {
        var difference = (dueDate.Date - today.Date).Days;
        if (difference > 0 && ParseBeforeDays(policy.BeforeDueDays).Contains(difference))
            return new FinancialReminderStage($"before-{difference}", difference == 1 ? "Vence amanhã" : $"Vence em {difference} dias");
        if (difference == 0 && policy.OnDueDate)
            return new FinancialReminderStage("due", "Vence hoje");
        var overdue = -difference;
        if (overdue >= policy.FirstOverdueDay && overdue <= policy.MaxOverdueDays &&
            (overdue - policy.FirstOverdueDay) % Math.Max(1, policy.RepeatEveryDays) == 0)
            return new FinancialReminderStage($"overdue-{overdue}", overdue == 1 ? "Vencida há 1 dia" : $"Vencida há {overdue} dias");
        return null;
    }

    internal static List<int> ParseBeforeDays(string value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var day) ? day : -1)
        .Where(x => x is >= 1 and <= 60).Distinct().OrderByDescending(x => x).ToList();

    internal static DateTime RunDate(DateTime month, int generationDay) =>
        DateTime.SpecifyKind(new DateTime(month.Year, month.Month, Math.Clamp(generationDay, 1, 28)), DateTimeKind.Utc);

    internal static string RecurringKey(Guid ruleId, DateTime competence, Guid unitId) =>
        $"rec:{ruleId:N}:{competence:yyyyMM}:{unitId:N}";

    internal static string Expand(string template, DateTime competence, string unit, int max)
    {
        var value = (template ?? string.Empty)
            .Replace("{competencia}", competence.ToString("MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")), StringComparison.OrdinalIgnoreCase)
            .Replace("{mes}", competence.ToString("MMMM", CultureInfo.GetCultureInfo("pt-BR")), StringComparison.OrdinalIgnoreCase)
            .Replace("{ano}", competence.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{unidade}", unit, StringComparison.OrdinalIgnoreCase).Trim();
        return value.Length <= max ? value : value[..max];
    }

    internal static string StageLabel(string key)
    {
        if (key == "due") return "Vence hoje";
        if (key.StartsWith("before-") && int.TryParse(key[7..], out var before)) return before == 1 ? "Vence amanhã" : $"Vence em {before} dias";
        if (key.StartsWith("overdue-") && int.TryParse(key[8..], out var overdue)) return overdue == 1 ? "Vencida há 1 dia" : $"Vencida há {overdue} dias";
        return "Atualização financeira";
    }

    private static DateTime Month(DateTime value) => DateTime.SpecifyKind(new DateTime(value.Year, value.Month, 1), DateTimeKind.Utc);
    private static DateTime Today(DateTime utcNow) => DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];

    internal sealed record FinancialReminderStage(string Key, string Label);
}

public sealed class FinancialAutomationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<FinancialAutomationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
        var minutes = Math.Clamp(configuration.GetValue("Financial:AutomationIntervalMinutes", 15), 5, 120);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RunAsync(stoppingToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            await scope.ServiceProvider.GetRequiredService<IFinancialAutomationRunner>().ProcessAsync(null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha na automação financeira"); }
    }
}

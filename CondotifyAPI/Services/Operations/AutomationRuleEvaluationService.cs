using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public interface IAutomationRuleEvaluationService
{
    Task<int> EvaluateAsync(Guid? ruleId = null, CancellationToken cancellationToken = default);
}

public sealed class AutomationRuleEvaluationService(
    DatabaseContext context,
    IIncidentService incidents,
    ILogger<AutomationRuleEvaluationService> logger) : IAutomationRuleEvaluationService
{
    public async Task<int> EvaluateAsync(Guid? ruleId = null, CancellationToken cancellationToken = default)
    {
        var rules = await context.AutomationRules.Include(x => x.License)
            .Where(x => x.IsEnabled && (!ruleId.HasValue || x.Id == ruleId.Value))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var triggered = 0;
        foreach (var rule in rules)
        {
            try
            {
                var signals = await CollectSignalsAsync(rule, DateTime.UtcNow, cancellationToken);
                foreach (var signal in signals.Take(20))
                    if (await TriggerAsync(rule, signal, cancellationToken)) triggered++;
                rule.LastEvaluatedAt = DateTime.UtcNow;
                rule.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                logger.LogWarning(exception, "Execucao concorrente descartada para a regra {RuleId}", rule.Id);
                context.ChangeTracker.Clear();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Falha ao avaliar a regra {RuleId}", rule.Id);
            }
        }
        return triggered;
    }

    private async Task<bool> TriggerAsync(AutomationRuleDTO rule, AutomationSignal signal, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var bucketTicks = TimeSpan.FromMinutes(Math.Clamp(rule.CooldownMinutes, 5, 43200)).Ticks;
        var fingerprint = $"rule:{rule.Id:N}:{signal.ResourceId:N}:{now.Ticks / bucketTicks}";
        if (await context.AutomationExecutions.AsNoTracking().AnyAsync(x => x.Fingerprint == fingerprint, cancellationToken))
            return false;

        Guid? incidentId = null;
        if (rule.Actions.HasFlag(AutomationActionEnum.CreateIncident))
        {
            var incident = await incidents.CreateAsync(new IncidentCreationRequest(
                rule.LicenseId, signal.Title, signal.Message, signal.Category, rule.Severity,
                IncidentSourceEnum.Automation, "Automacao Condotify", null, signal.ResourceType,
                signal.ResourceId, null, IncidentTimelineTypeEnum.Automation,
                System.Text.Json.JsonSerializer.Serialize(new { rule.Id, rule.Name, rule.TriggerType })), cancellationToken);
            incidentId = incident.Id;
        }

        Guid? alertId = null;
        if (rule.Actions.HasFlag(AutomationActionEnum.CreateAlert))
        {
            var alert = new OperationalAlertDTO
            {
                Id = Guid.NewGuid(), EnterpriseId = rule.License.EnterpriseId, LicenseId = rule.LicenseId,
                Fingerprint = fingerprint, Type = $"Automation{rule.TriggerType}", Source = "AutomationRule",
                Severity = ToAlertSeverity(rule.Severity), Status = OperationalAlertStatus.Open,
                Title = Short(signal.Title, 180), Message = Short(signal.Message, 1000),
                TargetUrl = $"/licencas/{rule.LicenseId}/ocorrencias", ResourceType = signal.ResourceType,
                ResourceId = signal.ResourceId, IsConditionActive = true, OccurrenceCount = 1,
                FirstOccurredAt = now, LastOccurredAt = now, CreatedAt = now, UpdatedAt = now
            };
            context.OperationalAlerts.Add(alert);
            alertId = alert.Id;
        }

        context.AutomationExecutions.Add(new AutomationExecutionDTO
        {
            Id = Guid.NewGuid(), RuleId = rule.Id, LicenseId = rule.LicenseId, Fingerprint = fingerprint,
            Status = "Triggered", Summary = Short(signal.Message, 1000), IncidentId = incidentId,
            AlertId = alertId, CreatedAt = now
        });
        rule.LastTriggeredAt = now;
        rule.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<List<AutomationSignal>> CollectSignalsAsync(
        AutomationRuleDTO rule,
        DateTime now,
        CancellationToken cancellationToken)
    {
        switch (rule.TriggerType)
        {
            case AutomationTriggerTypeEnum.DeviceOffline:
            {
                var cutoff = now.AddMinutes(-Math.Max(1, rule.Threshold));
                return await context.Devices.AsNoTracking()
                    .Where(x => x.LicenseId == rule.LicenseId && (!x.IsActive || !x.LastSeenAt.HasValue || x.LastSeenAt < cutoff))
                    .OrderBy(x => x.Name).Take(20)
                    .Select(x => new AutomationSignal(x.Id, "Device", IncidentCategoryEnum.Device,
                        $"{x.Name} esta offline", $"O equipamento {x.Name} nao se comunica ha pelo menos {rule.Threshold} minuto(s)."))
                    .ToListAsync(cancellationToken);
            }
            case AutomationTriggerTypeEnum.AccessDeniedThreshold:
            {
                var cutoff = now.AddMinutes(-Math.Max(1, rule.WindowMinutes));
                return await context.AccessEventRecords.AsNoTracking()
                    .Where(x => x.LicenseId == rule.LicenseId && !x.Authorized && x.OccurredAt >= cutoff)
                    .GroupBy(x => new { x.DeviceId, x.Device.Name })
                    .Where(group => group.Count() >= Math.Max(1, rule.Threshold))
                    .OrderByDescending(group => group.Count()).Take(20)
                    .Select(group => new AutomationSignal(group.Key.DeviceId, "Device", IncidentCategoryEnum.Access,
                        "Recusas de acesso acima do limite", $"{group.Count()} acessos foram recusados em {group.Key.Name} nos ultimos {rule.WindowMinutes} minuto(s)."))
                    .ToListAsync(cancellationToken);
            }
            case AutomationTriggerTypeEnum.VisitorOverstay:
            {
                var cutoff = now.AddMinutes(-Math.Max(0, rule.Threshold));
                return await context.AccessVisits.AsNoTracking()
                    .Where(x => x.LicenseId == rule.LicenseId && x.Status == AccessVisitStatusEnum.CheckedIn &&
                                (x.ExpectedCheckoutAt ?? x.ValidTo) < cutoff && !x.OverstayAcknowledgedAt.HasValue)
                    .OrderBy(x => x.ExpectedCheckoutAt ?? x.ValidTo).Take(20)
                    .Select(x => new AutomationSignal(x.Id, "Visit", IncidentCategoryEnum.Visitor,
                        $"Permanencia excedida: {x.VisitorName}",
                        $"A visita de {x.VisitorName} ultrapassou o horario previsto de saida."))
                    .ToListAsync(cancellationToken);
            }
            case AutomationTriggerTypeEnum.DeliveryOverdue:
            {
                var cutoff = now.AddMinutes(-Math.Max(1, rule.WindowMinutes));
                return await context.Deliveries.AsNoTracking()
                    .Where(x => x.LicenseId == rule.LicenseId && x.Status == DeliveryStatusEnum.Received &&
                                (x.ReceivedAt ?? x.CreatedAt) < cutoff)
                    .OrderBy(x => x.ReceivedAt ?? x.CreatedAt).Take(20)
                    .Select(x => new AutomationSignal(x.Id, "Delivery", IncidentCategoryEnum.Delivery,
                        $"Encomenda aguardando retirada", $"A encomenda {x.Name} aguarda retirada ha mais de {rule.WindowMinutes} minuto(s)."))
                    .ToListAsync(cancellationToken);
            }
            default:
                return [];
        }
    }

    private static OperationalAlertSeverity ToAlertSeverity(IncidentSeverityEnum severity) => severity switch
    {
        IncidentSeverityEnum.Critical or IncidentSeverityEnum.High => OperationalAlertSeverity.Critical,
        IncidentSeverityEnum.Medium => OperationalAlertSeverity.Warning,
        _ => OperationalAlertSeverity.Info
    };
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
    private sealed record AutomationSignal(Guid ResourceId, string ResourceType, IncidentCategoryEnum Category, string Title, string Message);
}

public sealed class AutomationRuleWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<AutomationRuleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EvaluateAsync(stoppingToken);
        var seconds = Math.Clamp(configuration.GetValue("Automation:EvaluationIntervalSeconds", 60), 30, 3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await EvaluateAsync(stoppingToken);
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            await scope.ServiceProvider.GetRequiredService<IAutomationRuleEvaluationService>().EvaluateAsync(null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha no motor de automacoes operacionais"); }
    }
}

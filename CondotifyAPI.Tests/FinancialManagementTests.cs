using System.Reflection;
using Condotify.Models;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class FinancialManagementTests
{
    [Fact]
    public void Calculator_UsesDecimalAndNeverReturnsNegativeTotal()
    {
        Assert.Equal(112.35m, FinancialChargeCalculator.Total(100m, 5.10m, 10.25m, 3m));
        Assert.Equal(0m, FinancialChargeCalculator.Total(10m, 0m, 0m, 50m));
    }

    [Theory]
    [InlineData(FinancialChargeStatus.Open, true)]
    [InlineData(FinancialChargeStatus.PaymentReported, true)]
    [InlineData(FinancialChargeStatus.Negotiated, true)]
    [InlineData(FinancialChargeStatus.Disputed, true)]
    [InlineData(FinancialChargeStatus.Paid, false)]
    [InlineData(FinancialChargeStatus.Cancelled, false)]
    public void Overdue_IsDerivedAndTerminalStatesNeverBecomeOverdue(FinancialChargeStatus status, bool expected)
    {
        var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, FinancialChargeCalculator.IsOverdue(status, now.AddDays(-10), now));
    }

    [Fact]
    public void Summary_BuildsAgingAndDistinctDelinquentUnits()
    {
        var now = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var unit = Guid.NewGuid();
        var rows = new[]
        {
            Row(unit, FinancialChargeStatusEnum.Open, now.AddDays(-10), 100m),
            Row(unit, FinancialChargeStatusEnum.Open, now.AddDays(-45), 200m),
            Row(Guid.NewGuid(), FinancialChargeStatusEnum.Open, now.AddDays(-100), 300m),
            Row(Guid.NewGuid(), FinancialChargeStatusEnum.Paid, now.AddDays(-5), 400m, now)
        };

        var result = FinancialManagementController.BuildSummary(rows, now);

        Assert.Equal(600m, result.OpenAmount);
        Assert.Equal(600m, result.OverdueAmount);
        Assert.Equal(400m, result.PaidThisMonthAmount);
        Assert.Equal(2, result.DelinquentUnits);
        Assert.Equal(new[] { 1, 1, 0, 1 }, result.Aging.Select(x => x.Count));
    }

    [Fact]
    public void RejectPaymentReport_RequiresThatExactCurrentState()
    {
        var input = new FinancialChargeActionViewModel { Action = FinancialChargeAction.RejectPaymentReport, Note = "Comprovante não localizado." };
        Assert.True(FinancialManagementController.ResolveAction(FinancialChargeStatusEnum.PaymentReported, input).Success);
        Assert.False(FinancialManagementController.ResolveAction(FinancialChargeStatusEnum.Open, input).Success);
    }

    [Fact]
    public void CancelAndReopen_RequireAnAuditReason()
    {
        var cancel = new FinancialChargeActionViewModel { Action = FinancialChargeAction.Cancel };
        var reopen = new FinancialChargeActionViewModel { Action = FinancialChargeAction.Reopen };
        Assert.False(FinancialManagementController.ResolveAction(FinancialChargeStatusEnum.Open, cancel).Success);
        Assert.False(FinancialManagementController.ResolveAction(FinancialChargeStatusEnum.Cancelled, reopen).Success);
    }

    [Fact]
    public void ResidentVisibility_RequiresLicenseAndUnitTogether()
    {
        var license = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var grant = new ResidentAccessGrant(Guid.NewGuid(), license, new[] { unit }, default, true);
        var predicate = ResidentFinancialController.IsVisibleTo(grant).Compile();

        Assert.True(predicate(new FinancialChargeDTO { LicenseId = license, UnitId = unit }));
        Assert.False(predicate(new FinancialChargeDTO { LicenseId = Guid.NewGuid(), UnitId = unit }));
        Assert.False(predicate(new FinancialChargeDTO { LicenseId = license, UnitId = Guid.NewGuid() }));
    }

    [Fact]
    public void Model_UsesMoneyPrecisionAndIdempotentRequestIndex()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(FinancialChargeDTO));
        Assert.NotNull(entity);
        Assert.Equal(18, entity!.FindProperty(nameof(FinancialChargeDTO.BaseAmount))!.GetPrecision());
        Assert.Equal(2, entity.FindProperty(nameof(FinancialChargeDTO.BaseAmount))!.GetScale());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(FinancialChargeDTO.LicenseId), nameof(FinancialChargeDTO.RequestKey)]));
    }

    [Theory]
    [InlineData(nameof(FinancialManagementController.GetOverview), LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(FinancialManagementController.GetCharge), LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(FinancialManagementController.CreateCharges), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialManagementController.UpdateCharge), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialManagementController.ApplyAction), LicensePermissionEnum.ManageFinance)]
    public void AdministrativeEndpoints_RequireExpectedPermission(string methodName, LicensePermissionEnum permission)
    {
        var method = typeof(FinancialManagementController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var attribute = Assert.Single(method!.GetCustomAttributes<RequireLicensePermissionAttribute>());
        Assert.Equal(permission, Assert.IsType<LicensePermissionEnum>(Assert.Single(attribute.Arguments!)));
    }

    [Fact]
    public void ResidentController_RequiresResidentPolicyAndIsNotAnonymous()
    {
        var authorize = typeof(ResidentFinancialController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Resident", authorize?.Policy);
        Assert.Null(typeof(ResidentFinancialController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/resident/financial", typeof(ResidentFinancialController).GetCustomAttribute<RouteAttribute>()?.Template);
    }

    [Fact]
    public void ResidentViewModel_HidesInternalAuditInformation()
    {
        var charge = new FinancialChargeDTO
        {
            Id = Guid.NewGuid(),
            UnitId = Guid.NewGuid(),
            Unit = new UnitDTO
            {
                Number = "101",
                Block = new BlockDTO { Name = "Bloco A" }
            },
            Notes = "Observação exclusiva da administração",
            PaymentReference = "Referência interna",
            CreatedBy = "administrador@condominio.local",
            UpdatedBy = "operador@condominio.local",
            Reference = "08/2026",
            Description = "Condomínio",
            DueDate = DateTime.UtcNow.AddDays(10),
            BaseAmount = 100m
        };

        var model = ResidentFinancialController.ToResidentViewModel(charge, DateTime.UtcNow);

        Assert.Empty(model.Notes);
        Assert.Empty(model.PaymentReference);
        Assert.Empty(model.CreatedBy);
        Assert.Empty(model.UpdatedBy);
        Assert.Equal("Bloco A / 101", model.UnitLabel);
    }

    [Fact]
    public void Recurrence_UsesStableKeysAndExpandsSafeTemplates()
    {
        var ruleId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var competence = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var key = FinancialAutomationRunner.RecurringKey(ruleId, competence, unitId);
        var reference = FinancialAutomationRunner.Expand("Condomínio {competencia} · {unidade}", competence, "101", 80);

        Assert.Equal(key, FinancialAutomationRunner.RecurringKey(ruleId, competence, unitId));
        Assert.True(key.Length <= 80);
        Assert.Equal("Condomínio 08/2026 · 101", reference);
    }

    [Fact]
    public void ReminderCadence_ResolvesBeforeDueAndRepeatedOverdueStages()
    {
        var policy = new FinancialReminderPolicyDTO
        {
            BeforeDueDays = "5,1",
            OnDueDate = true,
            FirstOverdueDay = 1,
            RepeatEveryDays = 7,
            MaxOverdueDays = 30
        };
        var today = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("before-5", FinancialAutomationRunner.ResolveStage(policy, today.AddDays(5), today)?.Key);
        Assert.Equal("due", FinancialAutomationRunner.ResolveStage(policy, today, today)?.Key);
        Assert.Equal("overdue-1", FinancialAutomationRunner.ResolveStage(policy, today.AddDays(-1), today)?.Key);
        Assert.Equal("overdue-8", FinancialAutomationRunner.ResolveStage(policy, today.AddDays(-8), today)?.Key);
        Assert.Null(FinancialAutomationRunner.ResolveStage(policy, today.AddDays(-9), today));
    }

    [Fact]
    public void ImportParser_ValidatesUnitsAmountsAndDuplicates()
    {
        var unit = new FinancialImportUnit(Guid.NewGuid(), "Bloco A", "101", "Bloco A / 101");
        var csv = "Bloco;Unidade;Competencia;Referencia;Descricao;Vencimento;Valor;Multa;Juros;Desconto;Observacoes\n" +
                  "Bloco A;101;2026-08;Condomínio 08/2026;Contribuição;10/08/2026;650,00;10,00;0,00;5,00;Teste\n" +
                  "Bloco A;101;2026-08;Condomínio 08/2026;Contribuição;10/08/2026;650,00;0,00;0,00;0,00;Duplicada";

        var result = new FinancialChargeImportCsvParser().Parse("financeiro.csv", csv, [unit]);

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(655m, result.TotalAmount);
        Assert.Contains(result.Rows[1].Messages, x => x.Contains("duplicada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AutomationModel_HasIdempotencyAndDeliveryIndexes()
    {
        using var context = CreateContext();
        var batch = context.Model.FindEntityType(typeof(FinancialImportBatchDTO));
        var delivery = context.Model.FindEntityType(typeof(FinancialReminderDeliveryDTO));
        var rule = context.Model.FindEntityType(typeof(FinancialRecurringRuleDTO));

        Assert.Contains(batch!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(FinancialImportBatchDTO.LicenseId), nameof(FinancialImportBatchDTO.IdempotencyKey)]));
        Assert.Contains(delivery!.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(FinancialReminderDeliveryDTO.LicenseId), nameof(FinancialReminderDeliveryDTO.DeliveryKey)]));
        Assert.Equal(18, rule!.FindProperty(nameof(FinancialRecurringRuleDTO.BaseAmount))!.GetPrecision());
        Assert.Equal(2, rule.FindProperty(nameof(FinancialRecurringRuleDTO.BaseAmount))!.GetScale());
    }

    [Theory]
    [InlineData(nameof(FinancialAutomationController.Get), LicensePermissionEnum.ViewFinance)]
    [InlineData(nameof(FinancialAutomationController.CreateRule), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialAutomationController.UpdateRule), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialAutomationController.UpdateReminderPolicy), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialAutomationController.PreviewImport), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialAutomationController.ExecuteImport), LicensePermissionEnum.ManageFinance)]
    [InlineData(nameof(FinancialAutomationController.RunNow), LicensePermissionEnum.ManageFinance)]
    public void AutomationEndpoints_RequireExpectedPermission(string methodName, LicensePermissionEnum permission)
    {
        var method = typeof(FinancialAutomationController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var attribute = Assert.Single(method!.GetCustomAttributes<RequireLicensePermissionAttribute>());
        Assert.Equal(permission, Assert.IsType<LicensePermissionEnum>(Assert.Single(attribute.Arguments!)));
    }

    private static FinancialManagementController.FinancialSummaryRow Row(Guid unitId, FinancialChargeStatusEnum status, DateTime dueDate, decimal amount, DateTime? paidAt = null) =>
        new(unitId, status, dueDate, paidAt, amount, 0m, 0m, 0m);

    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_Financial_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        return new DatabaseContext(options);
    }
}

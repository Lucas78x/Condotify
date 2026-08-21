using System.Reflection;
using Condotify.Models;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Operations;
using Microsoft.AspNetCore.Mvc;

namespace CondotifyAPI.Tests;

public sealed class MaintenanceManagementTests
{
    [Fact]
    public void WorkOrderDeadline_IsASelectedCalendarDate()
    {
        var selected = new DateTime(2026, 8, 21, 23, 45, 0, DateTimeKind.Local);

        var stored = selected.AsCalendarDate();

        Assert.Equal(new DateTime(2026, 8, 21), stored);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
    }

    [Theory]
    [InlineData(IncidentSeverityEnum.Low, 1440, 10080)]
    [InlineData(IncidentSeverityEnum.Medium, 480, 4320)]
    [InlineData(IncidentSeverityEnum.High, 120, 1440)]
    [InlineData(IncidentSeverityEnum.Critical, 30, 240)]
    public void DefaultSla_IsDeterministic(IncidentSeverityEnum severity, int response, int resolution)
    {
        var result = MaintenanceService.ResolveSla(null, severity);
        Assert.Equal(response, result.ResponseMinutes);
        Assert.Equal(resolution, result.ResolutionMinutes);
    }

    [Fact]
    public void PreventiveAdvance_SkipsMissedIntervals()
    {
        var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
        var result = MaintenanceService.Advance(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc), 30, now);
        Assert.True(result > now);
        Assert.Equal(new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ChecklistTemplate_RoundTripsRequiredFlag()
    {
        var json = MaintenanceService.ChecklistJson([
            new WorkOrderChecklistInputViewModel { Title = "Desligar circuito", IsRequired = true },
            new WorkOrderChecklistInputViewModel { Title = "Fotografar painel" }
        ]);
        var result = MaintenanceService.ParseChecklist(json);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsRequired);
        Assert.Equal("Fotografar painel", result[1].Title);
    }

    [Fact]
    public void MaintenanceMutations_RequireManageIncidents()
    {
        var methods = typeof(MaintenanceController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.GetCustomAttribute<HttpPostAttribute>() is not null || x.GetCustomAttribute<HttpPutAttribute>() is not null || x.GetCustomAttribute<HttpPatchAttribute>() is not null)
            .ToList();
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Contains(method.GetCustomAttributes<RequireLicensePermissionAttribute>(), _ => true));
    }

    [Fact]
    public void ResidentIncidentProjection_HidesInternalHistoryAndCosts()
    {
        var incident = new IncidentDTO
        {
            Id = Guid.NewGuid(), LicenseId = Guid.NewGuid(), Title = "Teste", Code = "INC-1",
            Timeline = [new IncidentTimelineEntryDTO { Id = Guid.NewGuid(), Message = "interno" }, new IncidentTimelineEntryDTO { Id = Guid.NewGuid(), Message = "visível", VisibleToResident = true }],
            WorkOrders = [new WorkOrderDTO { Id = Guid.NewGuid(), EstimatedCost = 100, ActualCost = 80 }]
        };
        var result = ResidentIncidentsController.ToResident(incident);
        Assert.Single(result.Timeline);
        Assert.Equal("visível", result.Timeline[0].Message);
        Assert.Equal(0, result.WorkOrders[0].ActualCost);
        Assert.Equal(0, result.WorkOrders[0].EstimatedCost);
    }
}

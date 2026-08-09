using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class DeviceHealthMonitoringWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DeviceHealthMonitoringWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("DeviceMonitoring:IntervalSeconds", 60), 15, 3600));
        await MonitorAsync(stoppingToken);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await MonitorAsync(stoppingToken);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();
            var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
            var thresholdMs = Math.Clamp(configuration.GetValue("DeviceMonitoring:ResponseWarningMs", 2500), 100, 30000);
            var devices = await context.Devices.ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                var wasOnline = device.IsActive;
                var previousResponse = device.LastResponseTimeMs;
                DeviceInspectionResult result;
                try { result = await accessControl.InspectAsync(mapper.Map<AccessControlDevice>(device)); }
                catch (Exception exception) { result = DeviceInspectionResult.Unavailable(exception.Message); }
                var now = DateTime.UtcNow;
                device.IsActive = result.Online; device.LastHealthCheckAt = now; device.LastUpdatedAt = now;
                device.LastSeenAt = result.Online ? now : device.LastSeenAt; device.LastResponseTimeMs = result.ResponseTimeMs;
                device.HealthMessage = result.Message; device.FirmwareVersion = result.FirmwareVersion ?? device.FirmwareVersion;
                device.SerialNumber = result.SerialNumber ?? device.SerialNumber;
                device.MACAddress = result.MacAddress?.ToUpperInvariant() ?? device.MACAddress;
                device.CapacityJson = Json(result.CapacityJson); device.DiscoveredPortalsJson = JsonSerializer.Serialize(result.Portals);
                if (wasOnline != result.Online)
                    context.AccessOperationAudits.Add(Alert(device, result.Online ? "DeviceRecovered" : "DeviceOffline", result.Online ? "Recovered" : "Alert", result.Message));
                if (result.Online && result.ResponseTimeMs > thresholdMs && (!previousResponse.HasValue || previousResponse <= thresholdMs))
                    context.AccessOperationAudits.Add(Alert(device, "DeviceSlow", "Warning", $"Tempo de resposta de {result.ResponseTimeMs} ms acima do limite de {thresholdMs} ms."));
            }
            if (context.ChangeTracker.HasChanges()) await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha no monitoramento de saude dos equipamentos"); }
    }

    private static AccessOperationAuditDTO Alert(AccessControlDeviceDTO device, string action, string status, string summary) => new()
    {
        Id = Guid.NewGuid(), LicenseId = device.LicenseId, EntityType = "Device", EntityId = device.Id, Action = action,
        Status = status, Summary = $"{device.Name}: {summary}"[..Math.Min(1000, $"{device.Name}: {summary}".Length)],
        DetailsJson = JsonSerializer.Serialize(new { device.IPAddress, device.Port, device.Model }), UserName = "Monitor de equipamentos", CreatedAt = DateTime.UtcNow
    };
    private static string Json(string value) { try { JsonDocument.Parse(value).Dispose(); return value; } catch { return "{}"; } }
}

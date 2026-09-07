using System.Net;
using Condotify.Models;
using Condotify.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Condotify.Web.Tests;

public sealed class ResidentHomeHighlightsTests
{
    [Fact]
    public async Task MissingSummary_ReportsUnavailableInsteadOfEmptySuccess()
    {
        var html = await Render(new() { [nameof(ResidentHomeHighlights.EnabledModules)] = (long)LicenseModuleEnum.All });
        Assert.Contains("Resumo indisponível", html);
        Assert.Contains("—", html);
        Assert.DoesNotContain(">0<", html);
    }

    [Fact]
    public async Task ServerModuleMask_HidesDisabledServicesAndUsesRealCounts()
    {
        var html = await Render(new()
        {
            [nameof(ResidentHomeHighlights.EnabledModules)] = (long)LicenseModuleEnum.All,
            [nameof(ResidentHomeHighlights.Summary)] = new ResidentHomeSummaryViewModel
            {
                GeneratedAt = DateTime.UtcNow, EnabledModules = (long)LicenseModuleEnum.Deliveries,
                DeliveriesAwaitingPickup = 2, VisitsToday = 1
            }
        });
        Assert.Contains("href=\"/deliveries\"", html);
        Assert.Contains(">2<", html);
        Assert.DoesNotContain("href=\"/financeiro\"", html);
        Assert.DoesNotContain("href=\"/bookings\"", html);
    }

    private static async Task<string> Render(Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoJs>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ResidentHomeHighlights>(ParameterView.FromDictionary(parameters));
            return WebUtility.HtmlDecode(rendered.ToHtmlString());
        });
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}

using System.Net;
using System.Text.Json;
using Condotify.Models;
using Condotify.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condotify.ApiClient.Tests;

public sealed class ResidentMobileClientTests
{
    [Fact]
    public async Task CreateResidentVisitAsync_UsesResidentRouteAndPreservesIdempotencyKey()
    {
        var handler = new CapturingHandler("{}");
        var client = CreateClient(handler);
        var key = "mobile-operation-123";

        await client.CreateResidentVisitAsync(new ResidentVisitFormViewModel
        {
            UnitId = Guid.NewGuid(),
            VisitorName = "Visitante",
            ValidFrom = DateTime.UtcNow.AddMinutes(5),
            ValidTo = DateTime.UtcNow.AddHours(2),
            IdempotencyKey = key
        });

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/resident/visits", handler.Uri?.AbsolutePath);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(key, json.RootElement.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task GetResidentAmenityAvailabilityAsync_UsesDateOnlyQuery()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler);
        var amenityId = Guid.NewGuid();

        await client.GetResidentAmenityAvailabilityAsync(amenityId, new DateTime(2026, 8, 17, 22, 10, 0));

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal($"/api/resident/amenities/{amenityId}/availability", handler.Uri?.AbsolutePath);
        Assert.Equal("?date=2026-08-17", handler.Uri?.Query);
    }

    [Fact]
    public async Task CreateResidentBookingAsync_UsesResidentScopedRoute()
    {
        var handler = new CapturingHandler("{}");
        var client = CreateClient(handler);
        var amenityId = Guid.NewGuid();

        await client.CreateResidentBookingAsync(amenityId, new AmenityBookingFormViewModel
        {
            UnitId = Guid.NewGuid(),
            Date = new DateTime(2026, 8, 20),
            SlotId = Guid.NewGuid(),
            TermsAccepted = true
        });

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/resident/amenities/{amenityId}/bookings", handler.Uri?.AbsolutePath);
    }

    [Fact]
    public async Task CancelResidentBookingAsync_SendsDeleteBodyWithReason()
    {
        var handler = new CapturingHandler(string.Empty, HttpStatusCode.NoContent);
        var client = CreateClient(handler);
        var bookingId = Guid.NewGuid();

        var result = await client.CancelResidentBookingAsync(bookingId, "Mudanca de planos");

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal($"/api/resident/bookings/{bookingId}", handler.Uri?.AbsolutePath);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Mudanca de planos", json.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GetResidentDeliveriesAsync_UsesResidentScopedRoute()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler);

        await client.GetResidentDeliveriesAsync();

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/resident/deliveries", handler.Uri?.AbsolutePath);
    }

    [Fact]
    public async Task GetResidentCamerasAsync_UsesResidentScopedRoute()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler);

        await client.GetResidentCamerasAsync();

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/resident/cameras", handler.Uri?.AbsolutePath);
    }

    [Fact]
    public async Task GetResidentCftvSnapshotAsync_UsesResidentScopedRoute()
    {
        var handler = new CapturingHandler("snapshot", HttpStatusCode.OK, "image/jpeg");
        var client = CreateClient(handler);
        var deviceId = Guid.NewGuid();

        var result = await client.GetResidentCftvSnapshotAsync(deviceId, 2);

        Assert.True(result.Success);
        Assert.StartsWith("data:image/jpeg;base64,", result.Value);
        Assert.Equal($"/api/resident/cameras/{deviceId}/snapshot", handler.Uri?.AbsolutePath);
        Assert.Equal("?channel=2", handler.Uri?.Query);
    }

    [Fact]
    public async Task OpenResidentCftvStreamAsync_DoesNotSendALicenseScope()
    {
        var handler = new CapturingHandler("{}");
        var client = CreateClient(handler);
        var deviceId = Guid.NewGuid();

        await client.OpenResidentCftvStreamAsync(deviceId, 2, "secondary", "webrtc");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/resident/cameras/{deviceId}/sessions", handler.Uri?.AbsolutePath);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(2, json.RootElement.GetProperty("channel").GetInt32());
    }

    [Fact]
    public async Task CloseResidentCftvStreamAsync_UsesResidentScopedRoute()
    {
        var handler = new CapturingHandler(string.Empty, HttpStatusCode.NoContent);
        var client = CreateClient(handler);
        var deviceId = Guid.NewGuid();

        var result = await client.CloseResidentCftvStreamAsync(deviceId, 3);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal($"/api/resident/cameras/{deviceId}/sessions/3", handler.Uri?.AbsolutePath);
    }

    private static CondotifyApiClient CreateClient(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CondotifyApi:BaseUrl"] = "https://localhost:7118" })
            .Build();
        return new CondotifyApiClient(factory, new SessionContext(), configuration, NullLogger<CondotifyApiClient>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SessionContext : ISessionContextProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("resident-token");
        public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }

    private sealed class CapturingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK, string contentType = "application/json") : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, contentType)
            };
        }
    }
}

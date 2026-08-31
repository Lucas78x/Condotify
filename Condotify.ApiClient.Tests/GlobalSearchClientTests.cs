using System.Net;
using Condotify.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condotify.ApiClient.Tests;

public sealed class GlobalSearchClientTests
{
    [Fact]
    public async Task SearchResidentsAsync_AddsEncodedVehiclePlateAndReadsMatches()
    {
        var handler = new CapturingHandler(
            """
            [{
              "id":"00000000-0000-0000-0000-000000000001",
              "licenseId":"00000000-0000-0000-0000-000000000002",
              "unitId":"00000000-0000-0000-0000-000000000003",
              "licenseName":"Condominio Teste",
              "name":"Pessoa Teste",
              "blockName":"Bloco A",
              "unitNumber":"101",
              "credentials":[],
              "vehicles":[{"plate":"ABC1D23","brand":"Fiat","model":"Argo","isActive":true}]
            }]
            """);
        var client = CreateClient(handler);

        var result = await client.SearchResidentsAsync(
            query: null,
            document: null,
            phone: null,
            credential: null,
            unit: null,
            licenseId: null,
            vehiclePlate: " abc 1d23 ");

        Assert.True(result.Success);
        Assert.Equal("ABC1D23", Assert.Single(Assert.Single(result.Value!).Vehicles).Plate);
        Assert.Contains("vehiclePlate=abc%201d23", handler.Uri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingSearchOverload_DoesNotSendVehiclePlate()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler);

        await client.SearchResidentsAsync("Pessoa", null, null, null, null, null);

        Assert.DoesNotContain("vehiclePlate", handler.Uri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    private static CondotifyApiClient CreateClient(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CondotifyApi:BaseUrl"] = "https://localhost:7118"
            })
            .Build();
        return new CondotifyApiClient(
            factory,
            new SessionContext(),
            configuration,
            NullLogger<CondotifyApiClient>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SessionContext : ISessionContextProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("test-token");

        public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

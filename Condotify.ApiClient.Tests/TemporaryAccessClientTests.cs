using System.Net;
using Condotify.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condotify.ApiClient.Tests;

public sealed class TemporaryAccessClientTests
{
    [Fact]
    public async Task GetRegistrationInvitesAsync_UsesLicenseScopedRouteAndReadsLifecycle()
    {
        var licenseId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var handler = new CapturingHandler(
            """
            [{
              "id":"00000000-0000-0000-0000-000000000001",
              "residentId":"00000000-0000-0000-0000-000000000002",
              "residentName":"Pessoa Teste",
              "contact":"pessoa@teste.com",
              "channel":"Link",
              "status":"Opened",
              "createdBy":"Operador",
              "sendCount":1,
              "sentAt":"2026-09-01T12:00:00Z",
              "expiresAt":"2026-09-08T12:00:00Z",
              "inviteUrl":"/cadastro/convite-token"
            }]
            """);
        var client = CreateClient(handler);

        var result = await client.GetRegistrationInvitesAsync(licenseId);

        Assert.True(result.Success);
        var invite = Assert.Single(result.Value!);
        Assert.Equal("Opened", invite.Status);
        Assert.Equal("/cadastro/convite-token", invite.InviteUrl);
        Assert.Equal($"/api/access/licenses/{licenseId}/registration-invites", handler.Uri!.AbsolutePath);
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
        return new CondotifyApiClient(factory, new SessionContext(), configuration, NullLogger<CondotifyApiClient>.Instance);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}

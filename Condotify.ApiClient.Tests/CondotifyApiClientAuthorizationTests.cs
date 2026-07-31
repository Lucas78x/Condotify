using System.Net;
using Condotify.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condotify.ApiClient.Tests;

public class CondotifyApiClientAuthorizationTests
{
    [Fact]
    public async Task GetLicensesAsync_AddsAuthorizationHeader_WhenTokenIsPresent()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler, new StubSessionContextProvider("jwt-abc-123"));

        await client.GetLicensesAsync();

        var request = handler.LastRequest;
        Assert.NotNull(request);
        Assert.NotNull(request!.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("jwt-abc-123", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetLicensesAsync_OmitsAuthorizationHeader_WhenTokenIsNull()
    {
        var handler = new CapturingHandler("[]");
        var client = CreateClient(handler, new StubSessionContextProvider(null));

        await client.GetLicensesAsync();

        var request = handler.LastRequest;
        Assert.NotNull(request);
        Assert.Null(request!.Headers.Authorization);
    }

    private static CondotifyApiClient CreateClient(HttpMessageHandler handler, ISessionContextProvider sessionContext)
    {
        var httpClient = new HttpClient(handler);
        var factory = new StubHttpClientFactory(httpClient);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CondotifyApi:BaseUrl"] = "https://localhost:7118"
            })
            .Build();

        return new CondotifyApiClient(
            factory,
            sessionContext,
            configuration,
            NullLogger<CondotifyApiClient>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubSessionContextProvider(string? accessToken) : ISessionContextProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(accessToken);

        public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(null);
    }

    private sealed class CapturingHandler(string jsonResponse) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}

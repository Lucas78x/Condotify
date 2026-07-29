using CondotifyAPI.Domain.Models;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.Drivers;
using System.Net;

namespace CondotifyAPI.Tests;

public sealed class ControlIdModelTests
{
    [Fact]
    public void TagFactory_ShouldPreserveRemoteIdentifiers()
    {
        var tag = ControlIdTagsModel.Create(12345, "E2000017221101441890ABCD", 67890);

        Assert.Equal(12345, tag.id);
        Assert.Equal("E2000017221101441890ABCD", tag.value);
        Assert.Equal(67890, tag.user_id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>unauthorized</html>")]
    [InlineData("{}")]
    [InlineData("""{"objects":{"uhf_tags":"invalid"}}""")]
    public void TagParser_ShouldIgnoreInvalidDeviceResponses(string response)
    {
        Assert.Empty(ControlIdAccessControlDriver.ParseUhfTags(response));
    }

    [Fact]
    public void TagParser_ShouldKeepValidTagsAndSkipMalformedItems()
    {
        const string response = """
            {
              "objects": {
                "uhf_tags": [
                  { "id": 11, "value": "TAG-11", "user_id": 22 },
                  { "id": 12, "value": null, "user_id": 23 },
                  { "value": "MISSING-ID", "user_id": 24 }
                ]
              }
            }
            """;

        var tag = Assert.Single(ControlIdAccessControlDriver.ParseUhfTags(response));
        Assert.Equal(11, tag.id);
        Assert.Equal("TAG-11", tag.value);
        Assert.Equal(22, tag.user_id);
    }

    [Fact]
    public async Task TestConnection_ShouldReturnFalseWhenLoginRespondsWithHtml()
    {
        var factory = new StubHttpClientFactory(new HttpClient(new StubHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>login proxy</html>")
            })));
        var driver = new ControlIdAccessControlDriver(factory);

        var result = await driver.TestConnectionAsync(new CreateAccessControlDeviceByLicenseIn
        {
            IPAddress = "127.0.0.1",
            Port = 80,
            Username = "admin",
            Password = "secret"
        });

        Assert.False(result);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}

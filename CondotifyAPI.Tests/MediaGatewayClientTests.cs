using System.Net;
using System.Text;
using CondotifyAPI.Services.CFTV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CondotifyAPI.Tests;

public class MediaGatewayClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static (MediaGatewayClient Client, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx:9997") };
        return (new MediaGatewayClient(http, NullLogger<MediaGatewayClient>.Instance), handler);
    }

    [Fact]
    public async Task EnsurePathAsync_PostsTheSourceOnDemand_AndReturnsTrue()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ok = await client.EnsurePathAsync("l1_d2_c1", "rtsp://u:p@10.0.0.1:554/live", CancellationToken.None);

        Assert.True(ok);
        Assert.Contains("/v3/config/paths/add/l1_d2_c1", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"sourceOnDemand\":true", handler.Bodies[0]);
        Assert.Contains("rtsp://u:p@10.0.0.1:554/live", handler.Bodies[0]);
    }

    [Fact]
    public async Task EnsurePathAsync_TreatsAnExistingPathAsSuccess()
    {
        // O MediaMTX devolve 400 quando o caminho ja existe; isso nao e erro
        // para o nosso fluxo, porque o caminho ja esta pronto para leitura.
        var (client, _) = Create(request => request.RequestUri!.ToString().Contains("/add/")
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("path already exists", Encoding.UTF8) }
            : new HttpResponseMessage(HttpStatusCode.OK));

        Assert.True(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePathAsync_ReturnsFalse_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.False(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePathAsync_ReturnsFalse_OnUnexpectedStatus()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.False(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task RemovePathAsync_CallsDelete_AndSwallowsFailure()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await client.RemovePathAsync("l1_d2_c1", CancellationToken.None);

        Assert.Contains("/v3/config/paths/delete/l1_d2_c1", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ActivePathCountAsync_ReadsItemCount()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"itemCount\":3,\"items\":[]}", Encoding.UTF8, "application/json")
        });

        Assert.Equal(3, await client.ActivePathCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ActivePathCountAsync_ReturnsZero_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.Equal(0, await client.ActivePathCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheRtspSource_IsNeverWrittenToTheLog()
    {
        // O logger nulo nao registra nada; este teste existe para travar a
        // intencao: se alguem trocar por um logger real e passar a URL, o
        // teste deve ser atualizado deliberadamente, nao por acidente.
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await client.EnsurePathAsync("l1_d2_c1", "rtsp://user:senha@10.0.0.1:554/live", CancellationToken.None);

        Assert.Single(handler.Bodies);
    }
}

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
    public async Task EnsurePathAsync_WhenAudioNormalizationIsEnabled_ConfiguresOnDemandFfmpegPublisher()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx:9997") };
        var client = new MediaGatewayClient(
            http,
            NullLogger<MediaGatewayClient>.Instance,
            normalizeAudio: true,
            internalSecret: "segredo interno");

        var ok = await client.EnsurePathAsync(
            "l1_d2_c1_m",
            "rtsp://u:p@10.0.0.1:554/live",
            CancellationToken.None);

        Assert.True(ok);
        Assert.Contains("\"source\":\"publisher\"", handler.Bodies[0]);
        Assert.Contains("-c:v copy", handler.Bodies[0]);
        Assert.Contains("-c:a libopus", handler.Bodies[0]);
        Assert.Contains("runOnDemand", handler.Bodies[0]);
        Assert.Contains("internal=segredo%20interno", handler.Bodies[0]);
    }

    [Fact]
    public async Task EnsurePathAsync_OnConflict_DeletesAndRetriesAdd_AndReturnsTrueWhenTheRetrySucceeds()
    {
        // O MediaMTX devolve 400 tanto quando o caminho ja existe quanto quando a
        // configuracao e invalida. Para uma origem rotacionada (ex.: senha trocada),
        // aceitar o 400 como sucesso deixaria a fonte antiga registrada para sempre.
        // O fluxo correto e apagar o caminho existente e tentar registrar de novo.
        var addAttempts = 0;
        var (client, handler) = Create(request =>
        {
            if (request.RequestUri!.ToString().Contains("/add/"))
            {
                addAttempts++;
                return addAttempts == 1
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("path already exists", Encoding.UTF8) }
                    : new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var ok = await client.EnsurePathAsync("l1_d2_c1_m", "rtsp://x", CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, addAttempts);
        Assert.Contains(handler.Requests, x => x.Method == HttpMethod.Delete && x.RequestUri!.ToString().Contains("/v3/config/paths/delete/l1_d2_c1_m"));
    }

    [Fact]
    public async Task EnsurePathAsync_OnConflict_ReturnsFalse_WhenTheRetryAlsoFails()
    {
        var (client, _) = Create(request => request.RequestUri!.ToString().Contains("/add/")
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("invalid configuration", Encoding.UTF8) }
            : new HttpResponseMessage(HttpStatusCode.OK));

        Assert.False(await client.EnsurePathAsync("l1_d2_c1_m", "rtsp://x", CancellationToken.None));
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

    private const string PathsListWithMixedLicensesAndViewers = """
        {
          "itemCount": 4,
          "items": [
            { "name": "l1111111111111111111111111111111_d1_c1_m", "ready": true, "readers": [{"type":"webRTCSession","id":"a"}] },
            { "name": "l1111111111111111111111111111111_d2_c1_m", "ready": false, "readers": [] },
            { "name": "l2222222222222222222222222222222_d1_c1_m", "ready": true, "readers": [{"type":"hlsMuxer","id":"b"}] },
            { "name": "l1111111111111111111111111111111_d3_c1_s", "ready": true, "readers": [{"type":"webRTCSession","id":"c"},{"type":"webRTCSession","id":"d"}] }
          ]
        }
        """;

    [Fact]
    public async Task ActiveViewerCountAsync_CountsOnlyPathsWithReaders_ForTheGivenLicensePrefix()
    {
        // Tres caminhos existem para a licenca 1111..., mas so dois tem readers de
        // fato (o segundo esta registrado e sem viewer nenhum). O caminho da
        // licenca 2222... nao deve contar, mesmo tendo um reader.
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PathsListWithMixedLicensesAndViewers, Encoding.UTF8, "application/json")
        });

        var count = await client.ActiveViewerCountAsync("l1111111111111111111111111111111_", CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ActiveViewerCountAsync_ReturnsZero_ForALicenseWithNoPaths()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PathsListWithMixedLicensesAndViewers, Encoding.UTF8, "application/json")
        });

        var count = await client.ActiveViewerCountAsync("l9999999999999999999999999999999_", CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ActiveViewerCountAsync_ReturnsZero_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.Equal(0, await client.ActiveViewerCountAsync("l1_", CancellationToken.None));
    }

    [Fact]
    public async Task ListPathsAsync_ReturnsNameReadyAndReaderCount_ForEveryPath()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PathsListWithMixedLicensesAndViewers, Encoding.UTF8, "application/json")
        });

        var paths = await client.ListPathsAsync(CancellationToken.None);

        Assert.Equal(4, paths.Count);
        var orphan = Assert.Single(paths, x => x.Name == "l1111111111111111111111111111111_d2_c1_m");
        Assert.False(orphan.Ready);
        Assert.Equal(0, orphan.ReaderCount);

        var busy = Assert.Single(paths, x => x.Name == "l1111111111111111111111111111111_d3_c1_s");
        Assert.True(busy.Ready);
        Assert.Equal(2, busy.ReaderCount);
    }

    [Fact]
    public async Task ListPathsAsync_ReturnsEmpty_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.Empty(await client.ListPathsAsync(CancellationToken.None));
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

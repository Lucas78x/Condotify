using System.Net;
using System.Text;
using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class HttpLprRecognitionClientTests
{
    [Fact]
    public async Task RecognizeAsync_ParsesPlateAndConfidence()
    {
        var handler = new StubHttpMessageHandler(new StringContent("""{"plate":"ABC1D23","confidence":0.87}""", Encoding.UTF8, "application/json"));
        var client = new HttpLprRecognitionClient(new HttpClient(handler) { BaseAddress = new Uri("http://lpr-ocr") });

        var result = await client.RecognizeAsync([1, 2, 3], "image/jpeg");

        Assert.Equal("ABC1D23", result.Plate);
        Assert.Equal(0.87, result.Confidence);
    }

    [Fact]
    public async Task RecognizeAsync_ReturnsNullPlate_WhenServiceFoundNothing()
    {
        var handler = new StubHttpMessageHandler(new StringContent("""{"plate":null,"confidence":0.0}""", Encoding.UTF8, "application/json"));
        var client = new HttpLprRecognitionClient(new HttpClient(handler) { BaseAddress = new Uri("http://lpr-ocr") });

        var result = await client.RecognizeAsync([1, 2, 3], "image/jpeg");

        Assert.Null(result.Plate);
        Assert.Equal(0.0, result.Confidence);
    }

    private sealed class StubHttpMessageHandler(HttpContent responseContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent });
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CondotifyAPI.Services.Lpr;

public sealed record PlateRecognitionResult(string? Plate, double Confidence);

public interface ILprRecognitionClient
{
    Task<PlateRecognitionResult> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default);
}

public sealed class HttpLprRecognitionClient(HttpClient httpClient) : ILprRecognitionClient
{
    public async Task<PlateRecognitionResult> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(imageContent, "file", "snapshot.jpg");

        using var response = await httpClient.PostAsync("/recognize", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RecognizeResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do servico de OCR.");

        return new PlateRecognitionResult(payload.Plate, payload.Confidence);
    }

    private sealed record RecognizeResponse(string? Plate, double Confidence);
}

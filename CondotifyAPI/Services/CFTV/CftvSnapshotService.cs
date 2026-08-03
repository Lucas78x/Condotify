using System.Net;
using CondotifyAPI.Domain.DTO.Equipments;

namespace CondotifyAPI.Services.CFTV;

public sealed record CftvSnapshot(byte[] Content, string ContentType);

public interface ICftvSnapshotService
{
    Task<CftvSnapshot?> FetchAsync(CFTVDeviceDTO device, int channel, CancellationToken cancellationToken = default);
}

public sealed class CftvSnapshotService : ICftvSnapshotService
{
    private const int MaxSnapshotBytes = 5 * 1024 * 1024;

    public async Task<CftvSnapshot?> FetchAsync(
        CFTVDeviceDTO device,
        int channel,
        CancellationToken cancellationToken = default)
    {
        channel = device.DeviceType == CFTVDeviceTypeEnum.Camera ? 1 : Math.Clamp(channel, 1, Math.Max(1, device.MaxChannels));
        if (!int.TryParse(new string(device.HTTPPort.Where(char.IsDigit).ToArray()), out var port)) port = 80;

        using var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(device.Username, device.Password),
            PreAuthenticate = true,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

        foreach (var candidate in CandidatePaths(device.Mark, channel))
        {
            try
            {
                var uri = BuildUri(device.IpAddress, port, candidate);
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
                var content = await ReadLimitedAsync(response.Content, cancellationToken);
                if (content is not null && IsSupportedImage(content))
                    return new CftvSnapshot(content, contentType);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
            {
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> CandidatePaths(MarkEnum mark, int channel)
    {
        var candidates = mark switch
        {
            MarkEnum.Hikvision or MarkEnum.Hilook => new[] { $"/ISAPI/Streaming/channels/{channel}01/picture" },
            MarkEnum.Dahua or MarkEnum.Intelbras => new[]
            {
                $"/cgi-bin/snapshot.cgi?channel={channel}",
                $"/cgi-bin/snapshot.cgi?channel={Math.Max(0, channel - 1)}"
            },
            MarkEnum.Axis => new[] { $"/axis-cgi/jpg/image.cgi?camera={channel}" },
            MarkEnum.Uniview => new[] { $"/LAPI/V1.0/Channels/{channel}/Media/Video/Streams/0/Snapshot" },
            _ => Array.Empty<string>()
        };

        return candidates.Concat(new[] { "/snapshot.jpg", "/snap.jpg" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Uri BuildUri(string host, int port, string pathAndQuery)
    {
        var separator = pathAndQuery.IndexOf('?');
        var path = separator < 0 ? pathAndQuery : pathAndQuery[..separator];
        var query = separator < 0 ? string.Empty : pathAndQuery[(separator + 1)..];
        var builder = new UriBuilder(Uri.UriSchemeHttp, host, port, path) { Query = query };
        return builder.Uri;
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxSnapshotBytes) return null;
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaxSnapshotBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }

    private static bool IsSupportedImage(byte[] content) =>
        content.Length >= 4 &&
        ((content[0] == 0xFF && content[1] == 0xD8) ||
         (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47));
}

using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;


namespace CondotifyAPI.Services.CFTV
{

    public class CFTVService : ICFTVService
    {
        // Se você quiser, mova isso pra appsettings: default RTSP username
        private const string DefaultRtspUser = "admin";

        private static readonly Dictionary<MarkEnum, string[]> RtspPathsByBrand = new()
        {
            // Intelbras / Dahua-like (muitos modelos)
            [MarkEnum.Intelbras] = new[]
            {
                "/cam/realmonitor?channel=1&subtype=0",
                "/cam/realmonitor?channel=1&subtype=1",
                "/live",
                "/h264"
            },

            [MarkEnum.Dahua] = new[]
            {
                "/cam/realmonitor?channel=1&subtype=0",
                "/cam/realmonitor?channel=1&subtype=1",
            },

            // Hikvision / Hilook
            [MarkEnum.Hikvision] = new[]
            {
                "/Streaming/Channels/101",
                "/Streaming/Channels/102",
                "/h264/ch1/main/av_stream",
                "/h264/ch1/sub/av_stream",
            },
            [MarkEnum.Hilook] = new[]
            {
                "/Streaming/Channels/101",
                "/Streaming/Channels/102",
                "/h264/ch1/main/av_stream",
                "/h264/ch1/sub/av_stream",
            },

            // Uniview
            [MarkEnum.Uniview] = new[]
            {
                "/live/0/main",
                "/live/0/sub",
                "/live/ch00_0",
                "/live/ch00_1",
            },

            // Axis
            [MarkEnum.Axis] = new[]
            {
                "/axis-media/media.amp",
                "/axis-media/media.amp?videocodec=h264",
            },
        };

        private static readonly string[] GenericRtspPaths = new[]
        {
            "/cam/realmonitor?channel=1&subtype=0",
            "/cam/realmonitor?channel=1&subtype=1",
            "/Streaming/Channels/101",
            "/Streaming/Channels/102",
            "/h264/ch1/main/av_stream",
            "/h264/ch1/sub/av_stream",
            "/live",
            "/stream1",
            "/stream2",
            "/h264",
        };

        public async Task<TestCftvConnectionOut> TestAsync(CFTVDevice device,CancellationToken ct = default)
        {
            var result = new TestCftvConnectionOut();

            // 1️⃣ Ping
            result.PingOk = await PingAsync(device.IpAddress, 1500);

            // 2️⃣ Porta RTSP
            int rtspPort = ParsePortOrDefault(device.RTSPPort, 554);

            // 3️⃣ TCP RTSP
            result.TcpRtspOk = await TcpPortOpenAsync(device.IpAddress, rtspPort, 1200);
            if (!result.TcpRtspOk)
                return result;

            // 🔀 Fluxo por tipo de device
            if (device.DeviceType == CFTVDeviceTypeEnum.Camera)
            {
                // 📸 CÂMERA → um único teste
                var channelResult = await TestCameraAsync(device, rtspPort, ct);
                result.Channels.Add(channelResult);
            }
            else
            {
                foreach (var ch in device.Channels.Distinct())
                {
                    ct.ThrowIfCancellationRequested();

                    var channelResult = await TestChannelAsync(
                        device,
                        ch.ChannelNumber,
                        rtspPort,
                        ct
                    );

                    result.Channels.Add(channelResult);
                }
            }

            return result;
        }
        private async Task<ChannelTestResultOut> TestCameraAsync(
         CFTVDevice device,
         int rtspPort,
         CancellationToken ct)
        {
            var channelResult = new ChannelTestResultOut
            {
                ChannelNumber = 1 // padrão
            };

            var templates = GetCameraTemplates(device.Mark);

            foreach (var path in templates)
            {
                ct.ThrowIfCancellationRequested();

                var url = BuildRtspUrl(
                    device.IpAddress,
                    rtspPort,
                    device.UserName,
                    device.Password,
                    path
                );

                channelResult.Attempts.Add(url);

                var (okOpt, _) = await RtspOptionsAsync(
                    device.IpAddress, rtspPort, url, 2200);

                if (okOpt)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = url;
                    return channelResult;
                }

                var (okDesc, _) = await RtspDescribeAsync(
                    device.IpAddress, rtspPort, url, 2600);

                if (okDesc)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = url;
                    return channelResult;
                }
            }

            channelResult.Error = "Nenhum RTSP respondeu (Camera)";
            return channelResult;
        }

        private async Task<ChannelTestResultOut> TestChannelAsync(
         CFTVDevice device,
         int channel,
         int rtspPort,
         CancellationToken ct)
        {
            var channelResult = new ChannelTestResultOut
            {
                ChannelNumber = channel
            };

            var templates = GetDvrTemplates(device.Mark);

            foreach (var tpl in templates)
            {
                ct.ThrowIfCancellationRequested();

                var path = tpl.Replace("{ch}", channel.ToString("D2"));

                var url = BuildRtspUrl(
                    device.IpAddress,
                    rtspPort,
                    device.UserName,
                    device.Password,
                    path
                );

                channelResult.Attempts.Add(url);

                var (okOpt, _) = await RtspOptionsAsync(
                    device.IpAddress, rtspPort, url, 2200);

                if (okOpt)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = url;
                    return channelResult;
                }

                var (okDesc, _) = await RtspDescribeAsync(
                    device.IpAddress, rtspPort, url, 2600);

                if (okDesc)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = url;
                    return channelResult;
                }
            }

            channelResult.Error = $"Nenhum RTSP respondeu no canal {channel}";
            return channelResult;
        }

        private static IEnumerable<string> GetCameraTemplates(MarkEnum mark)
        {
            return mark switch
            {
                MarkEnum.Axis => new[]
                {
            "/axis-media/media.amp",
            "/axis-media/media.amp?videocodec=h264"
        },

                _ => new[]
                {
            "/live",
            "/stream1",
            "/h264"
        }
            };
        }

        private static IEnumerable<string> GetDvrTemplates(MarkEnum mark)
        {
            if (RtspPathTemplatesByBrand.TryGetValue(mark, out var paths))
                return paths;

            return new[]
            {
              "/cam/realmonitor?channel={ch}&subtype=0",
              "/Streaming/Channels/{ch}01"
            };
        }
        private static readonly Dictionary<MarkEnum, string[]> RtspPathTemplatesByBrand = new()
        {
            [MarkEnum.Intelbras] = new[]
            {
                "/cam/realmonitor?channel={ch}&subtype=0",
                "/cam/realmonitor?channel={ch}&subtype=1",
            },

            [MarkEnum.Dahua] = new[]
            {
                "/cam/realmonitor?channel={ch}&subtype=0",
                "/cam/realmonitor?channel={ch}&subtype=1",
            },

            [MarkEnum.Hikvision] = new[]
            {
                "/Streaming/Channels/{ch}01",
                "/Streaming/Channels/{ch}02",
                "/h264/ch{ch}/main/av_stream",
                "/h264/ch{ch}/sub/av_stream",
            },

            [MarkEnum.Hilook] = new[]
            {
                "/Streaming/Channels/{ch}01",
                "/Streaming/Channels/{ch}02",
            },

            [MarkEnum.Uniview] = new[]
            {
                "/live/ch{ch}_0",
                "/live/ch{ch}_1",
            },

            [MarkEnum.Axis] = new[]
            {
                "/axis-media/media.amp"
            }
        };

        private static int ParsePortOrDefault(string? port, int defaultPort)
        {
            if (string.IsNullOrWhiteSpace(port)) return defaultPort;

            // aceita "554" ou " :554 " etc
            var digits = new string(port.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var p) && p > 0 && p <= 65535)
                return p;

            return defaultPort;
        }

        private static string BuildRtspUrl(string ip, int port, string user, string pass, string path)
        {
            if (!path.StartsWith("/")) path = "/" + path;

            var u = Uri.EscapeDataString(user);
            var p = Uri.EscapeDataString(pass ?? "");

            return $"rtsp://{u}:{p}@{ip}:{port}{path}";
        }

        private static async Task<bool> PingAsync(string ip, int timeoutMs)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, timeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch { return false; }
        }

        private static async Task<bool> TcpPortOpenAsync(string ip, int port, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                return done == connectTask && client.Connected;
            }
            catch { return false; }
        }

        private static async Task<(bool ok, string raw)> RtspOptionsAsync(string ip, int port, string fullUrl, int timeoutMs)
        {
            var request =
                $"OPTIONS {fullUrl} RTSP/1.0\r\n" +
                $"CSeq: 1\r\n" +
                $"User-Agent: Condotify-RTSP-Test\r\n\r\n";

            return await SendRtspAsync(ip, port, request, timeoutMs);
        }

        private static async Task<(bool ok, string raw)> RtspDescribeAsync(string ip, int port, string fullUrl, int timeoutMs)
        {
            var request =
                $"DESCRIBE {fullUrl} RTSP/1.0\r\n" +
                $"CSeq: 2\r\n" +
                $"Accept: application/sdp\r\n" +
                $"User-Agent: Condotify-RTSP-Test\r\n\r\n";

            return await SendRtspAsync(ip, port, request, timeoutMs);
        }

        private static async Task<(bool ok, string raw)> SendRtspAsync(string ip, int port, string request, int timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                if (done != connectTask || !client.Connected)
                    return (false, "TCP connect timeout/fail");

                using var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                stream.WriteTimeout = timeoutMs;

                var data = Encoding.ASCII.GetBytes(request);
                await stream.WriteAsync(data, 0, data.Length);

                var buffer = new byte[8192];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);

                var resp = Encoding.ASCII.GetString(buffer, 0, read);

                // 200 OK => RTSP ok
                // 401 Unauthorized => RTSP ok, credencial errada (mas respondeu)
                // 404 Not Found => RTSP ok, path errado
                bool ok =
                    resp.Contains("RTSP/1.0 200") ||
                    resp.Contains("RTSP/1.0 401") ||
                    resp.Contains("RTSP/1.0 404");

                return (ok, resp);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}

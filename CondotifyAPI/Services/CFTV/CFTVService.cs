using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;


namespace CondotifyAPI.Services.CFTV
{

    public class CFTVService : ICFTVService
    {
        private const string DefaultRtspUser = "admin";

        private readonly ICftvStreamPathResolver _pathResolver;

        public CFTVService(ICftvStreamPathResolver pathResolver)
        {
            _pathResolver = pathResolver;
        }

        public async Task<TestCftvConnectionOut> TestAsync(CFTVDevice device, CancellationToken ct = default)
        {
            var result = new TestCftvConnectionOut();

            result.PingOk = await PingAsync(device.IpAddress, 1500);

            int rtspPort = ParsePortOrDefault(device.RTSPPort, 554);

            result.TcpRtspOk = await TcpPortOpenAsync(device.IpAddress, rtspPort, 1200);
            if (!result.TcpRtspOk)
                return result;

            if (device.DeviceType == CFTVDeviceTypeEnum.Camera)
            {
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
                ChannelNumber = 1
            };

            var templates = _pathResolver.ConnectivityProbePaths(device.Mark, device.DeviceType, 1);

            foreach (var path in templates)
            {
                ct.ThrowIfCancellationRequested();

                var url = _pathResolver.BuildRtspUrl(
                    device.IpAddress,
                    rtspPort,
                    device.UserName,
                    device.Password,
                    path
                );

                channelResult.Attempts.Add(RtspUrlMasker.Mask(url));

                var (okOpt, _) = await RtspOptionsAsync(
                    device.IpAddress, rtspPort, url, 2200);

                if (okOpt)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = RtspUrlMasker.Mask(url);
                    return channelResult;
                }

                var (okDesc, _) = await RtspDescribeAsync(
                    device.IpAddress, rtspPort, url, 2600);

                if (okDesc)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = RtspUrlMasker.Mask(url);
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

            var templates = _pathResolver.ConnectivityProbePaths(device.Mark, device.DeviceType, channel);

            foreach (var path in templates)
            {
                ct.ThrowIfCancellationRequested();

                var url = _pathResolver.BuildRtspUrl(
                    device.IpAddress,
                    rtspPort,
                    device.UserName,
                    device.Password,
                    path
                );

                channelResult.Attempts.Add(RtspUrlMasker.Mask(url));

                var (okOpt, _) = await RtspOptionsAsync(
                    device.IpAddress, rtspPort, url, 2200);

                if (okOpt)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = RtspUrlMasker.Mask(url);
                    return channelResult;
                }

                var (okDesc, _) = await RtspDescribeAsync(
                    device.IpAddress, rtspPort, url, 2600);

                if (okDesc)
                {
                    channelResult.RtspOk = true;
                    channelResult.RtspUrlWorking = RtspUrlMasker.Mask(url);
                    return channelResult;
                }
            }

            channelResult.Error = $"Nenhum RTSP respondeu no canal {channel}";
            return channelResult;
        }

        private static int ParsePortOrDefault(string? port, int defaultPort)
        {
            if (string.IsNullOrWhiteSpace(port)) return defaultPort;

            var digits = new string(port.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var p) && p > 0 && p <= 65535)
                return p;

            return defaultPort;
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

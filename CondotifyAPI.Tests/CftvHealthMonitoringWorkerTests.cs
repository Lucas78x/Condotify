using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

// Estes dois metodos sao a unica logica de CftvHealthMonitoringWorker que nao depende de
// banco de dados nem de rede (TCP), por isso sao os unicos testados aqui sem infraestrutura.
public class CftvHealthMonitoringWorkerTests
{
    [Theory]
    [InlineData("554", 554)]
    [InlineData("8554", 8554)]
    [InlineData("554/tcp", 554)]
    [InlineData("", 554)]
    [InlineData(null, 554)]
    [InlineData("abc", 554)]
    [InlineData("0", 554)]
    [InlineData("999999", 554)]
    public void ParsePort_FallsBackToDefault_WhenNoValidPortDigitsArePresent(string? rawPort, int expected)
    {
        Assert.Equal(expected, CftvHealthMonitoringWorker.ParsePort(rawPort, CftvHealthMonitoringWorker.DefaultRtspPort));
    }

    [Fact]
    public void ParsePort_UsesCustomFallback_WhenProvided()
    {
        Assert.Equal(80, CftvHealthMonitoringWorker.ParsePort("not-a-port", 80));
    }

    [Fact]
    public void DescribeHealth_ReturnsEmptyMessage_WhenReachable()
    {
        Assert.Equal(string.Empty, CftvHealthMonitoringWorker.DescribeHealth(reachable: true));
    }

    [Fact]
    public void DescribeHealth_ReturnsOfflineMessage_WhenUnreachable()
    {
        var message = CftvHealthMonitoringWorker.DescribeHealth(reachable: false);

        Assert.Equal(CftvHealthMonitoringWorker.OfflineMessage, message);
    }

    [Fact]
    public void DescribeHealth_NeverExposesIpOrPortOrCredential()
    {
        var offline = CftvHealthMonitoringWorker.DescribeHealth(reachable: false);
        var online = CftvHealthMonitoringWorker.DescribeHealth(reachable: true);

        foreach (var message in new[] { offline, online })
        {
            Assert.DoesNotContain(":", message); // sem host:porta embutido
            Assert.DoesNotMatch(@"\d", message); // nenhum digito (IP/porta) na mensagem
        }
    }
}

using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

// So a logica de casamento de nome e o criterio de orfandade sao testados aqui sem
// infraestrutura -- o laco do BackgroundService em si depende do gateway real.
public class CftvPathReaperWorkerTests
{
    [Theory]
    [InlineData("l11111111111111111111111111111111_d22222222222222222222222222222222_c1_m", true)]
    [InlineData("l11111111111111111111111111111111_d22222222222222222222222222222222_c12_s", true)]
    [InlineData("l11111111111111111111111111111111_d22222222222222222222222222222222_c1", false)] // sem sufixo de qualidade (formato antigo)
    [InlineData("l11111111111111111111111111111111_d22222222222222222222222222222222_c1_x", false)] // qualidade invalida
    [InlineData("probecam", false)]
    [InlineData("", false)]
    [InlineData("L11111111111111111111111111111111_d22222222222222222222222222222222_c1_m", false)] // maiuscula
    [InlineData("l111_d22222222222222222222222222222222_c1_m", false)] // licenca curta demais
    public void MatchesCondotifyNaming_OnlyAcceptsTheExactCondotifyShape(string name, bool expected)
    {
        Assert.Equal(expected, CftvPathReaperWorker.MatchesCondotifyNaming(name));
    }

    [Fact]
    public void IsOrphaned_IsTrue_OnlyWhenNotReady_NoReaders_AndCondotifyNamed()
    {
        var condotifyName = "l11111111111111111111111111111111_d22222222222222222222222222222222_c1_m";

        Assert.True(CftvPathReaperWorker.IsOrphaned(new GatewayPathState(condotifyName, Ready: false, ReaderCount: 0)));
    }

    [Fact]
    public void IsOrphaned_IsFalse_WhenThereIsAtLeastOneReader()
    {
        var condotifyName = "l11111111111111111111111111111111_d22222222222222222222222222222222_c1_m";

        Assert.False(CftvPathReaperWorker.IsOrphaned(new GatewayPathState(condotifyName, Ready: true, ReaderCount: 1)));
    }

    [Fact]
    public void IsOrphaned_IsFalse_WhenTheNameDoesNotMatchTheCondotifyPattern()
    {
        // Nunca reaper um caminho arbitrario, mesmo se ele parecer ocioso: apenas
        // caminhos que o proprio Condotify registrou podem ser removidos.
        Assert.False(CftvPathReaperWorker.IsOrphaned(new GatewayPathState("probecam", Ready: false, ReaderCount: 0)));
    }

    [Fact]
    public void IsOrphaned_IsFalse_WhenReadyButWithoutReaders()
    {
        // "ready" sem leitores pode ser uma leitura em andamento cujo reader ainda
        // nao foi contabilizado; so remove quando nem sequer esta pronto.
        var condotifyName = "l11111111111111111111111111111111_d22222222222222222222222222222222_c1_m";

        Assert.False(CftvPathReaperWorker.IsOrphaned(new GatewayPathState(condotifyName, Ready: true, ReaderCount: 0)));
    }
}

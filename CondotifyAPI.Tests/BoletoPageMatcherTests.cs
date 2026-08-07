using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Services.Finance;

namespace CondotifyAPI.Tests;

public sealed class BoletoPageMatcherTests
{
    private static readonly Guid UnitA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnitB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly List<BoletoPageMatcher.ResidentCandidate> OneResident =
    [
        new(Guid.NewGuid(), "12345678901", UnitA)
    ];

    private static readonly List<BoletoPageMatcher.UnitCandidate> Units =
    [
        new(UnitA, "Bloco A", "101"),
        new(UnitB, "Bloco A", "102")
    ];

    [Fact]
    public void Match_ByCpf_WithPunctuation_Matches()
    {
        var result = BoletoPageMatcher.Match("Sacado: Maria Silva CPF: 123.456.789-01", OneResident, Units);

        Assert.Equal(UnitA, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.Cpf, result.Method);
    }

    [Fact]
    public void Match_ByCpf_WithoutPunctuation_Matches()
    {
        var result = BoletoPageMatcher.Match("Sacado: Maria Silva CPF: 12345678901", OneResident, Units);

        Assert.Equal(UnitA, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.Cpf, result.Method);
    }

    [Fact]
    public void Match_WithTwoDifferentCandidateCpfsOnSamePage_FallsBackToUnitText()
    {
        var residents = new List<BoletoPageMatcher.ResidentCandidate>
        {
            new(Guid.NewGuid(), "12345678901", UnitA),
            new(Guid.NewGuid(), "98765432100", UnitB)
        };
        var text = "CPF 123.456.789-01 CPF 987.654.321-00 Apto 101 Bloco A";

        var result = BoletoPageMatcher.Match(text, residents, Units);

        Assert.Equal(UnitA, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.UnitText, result.Method);
    }

    [Theory]
    [InlineData("Unidade: Apto 101 - Bloco A")]
    [InlineData("Unidade 101 Bloco A")]
    [InlineData("Ap. 101 - Bloco A")]
    public void Match_ByUnitText_VariousPhrasings_Matches(string text)
    {
        var result = BoletoPageMatcher.Match(text, [], Units);

        Assert.Equal(UnitA, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.UnitText, result.Method);
    }

    [Fact]
    public void Match_UnitNumberWithoutBlock_StillMatchesWhenUnambiguous()
    {
        var soleUnit = new List<BoletoPageMatcher.UnitCandidate> { new(UnitA, "Bloco A", "101") };

        var result = BoletoPageMatcher.Match("Apto 101", [], soleUnit);

        Assert.Equal(UnitA, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.UnitText, result.Method);
    }

    [Fact]
    public void Match_SameUnitNumberInDifferentBlocks_DisambiguatesByBlockText()
    {
        var overlapping = new List<BoletoPageMatcher.UnitCandidate>
        {
            new(UnitA, "Bloco A", "101"),
            new(UnitB, "Bloco B", "101")
        };

        var result = BoletoPageMatcher.Match("Unidade 101 Bloco B", [], overlapping);

        Assert.Equal(UnitB, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.UnitText, result.Method);
    }

    [Fact]
    public void Match_SameUnitNumberInDifferentBlocks_NoBlockText_ReturnsUnmatched()
    {
        var overlapping = new List<BoletoPageMatcher.UnitCandidate>
        {
            new(UnitA, "Bloco A", "101"),
            new(UnitB, "Bloco B", "101")
        };

        var result = BoletoPageMatcher.Match("Unidade 101", [], overlapping);

        Assert.Null(result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.Unmatched, result.Method);
    }

    [Fact]
    public void Match_NoCpfAndNoUnitText_ReturnsUnmatched()
    {
        var result = BoletoPageMatcher.Match("Folha de capa sem identificacao", OneResident, Units);

        Assert.Null(result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.Unmatched, result.Method);
    }

    [Fact]
    public void Match_EmptyText_ReturnsUnmatched()
    {
        var result = BoletoPageMatcher.Match(string.Empty, OneResident, Units);

        Assert.Null(result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.Unmatched, result.Method);
    }

    [Fact]
    public void Match_CpfPresentButUnknown_FallsBackToUnitText()
    {
        var text = "CPF 111.222.333-44 Apto 102 Bloco A";

        var result = BoletoPageMatcher.Match(text, OneResident, Units);

        Assert.Equal(UnitB, result.UnitId);
        Assert.Equal(BoletoMatchMethodEnum.UnitText, result.Method);
    }
}

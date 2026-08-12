using CondotifyAPI.Services.Imports;

namespace CondotifyAPI.Tests;

public sealed class StructureImportCsvParserTests
{
    private readonly StructureImportCsvParser _parser = new();

    [Fact]
    public void Parse_ShouldReadSemicolonFileWithBomAliasesAndQuotedFields()
    {
        const string csv = "\uFEFFQuadra;Lote;Nome;Categoria;Vinculo;CPF;Placa;Tag\n" +
                           "Q1;10;\"Maria; da Silva\";Responsavel;Proprietario;123.456.789-01;abc-1d23;TAG-01";

        var result = _parser.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("Q1", row.Block);
        Assert.Equal("10", row.Unit);
        Assert.Equal("Maria; da Silva", row.Name);
        Assert.Equal("12345678901", row.CPF);
        Assert.Equal("ABC1D23", row.Plate);
    }

    [Fact]
    public void Parse_ShouldReadCommaSeparatedFile()
    {
        const string csv = "Bloco,Unidade,Nome,Email\nA,101,\"Ana Souza\",ana@example.com";

        var result = _parser.Parse(csv);

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("A", row.Block);
        Assert.Equal("101", row.Unit);
        Assert.Equal("Ana Souza", row.Name);
    }

    [Theory]
    [InlineData("Relação", "relacao")]
    [InlineData("  QUADRA A  ", "quadraa")]
    [InlineData("João-101", "joao101")]
    public void NormalizeKey_ShouldIgnoreAccentsPunctuationAndCase(string value, string expected)
    {
        Assert.Equal(expected, StructureImportCsvParser.NormalizeKey(value));
    }

    [Fact]
    public void Parse_ShouldRejectMissingRequiredHeaders()
    {
        const string csv = "Nome;CPF\nAna;12345678901";

        var result = _parser.Parse(csv);

        Assert.Contains(result.Errors, x => x.Contains("Agrupamento", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("Unidade", StringComparison.Ordinal));
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ShouldRejectEmptyFiles(string content)
    {
        var result = _parser.Parse(content);

        Assert.Equal("O arquivo esta vazio.", Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_ShouldRejectFilesOverTheRowLimit()
    {
        var rows = Enumerable.Range(1, 1_001).Select(x => $"A;{x}");
        var csv = "Bloco;Unidade\n" + string.Join('\n', rows);

        var result = _parser.Parse(csv);

        Assert.Equal(1_000, result.Rows.Count);
        Assert.Contains(result.Errors, x => x.Contains("mais de 1000", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ShouldRejectFilesOverTwoMegabytes()
    {
        var content = "Bloco;Unidade\n" + new string('A', 2_000_001);

        var result = _parser.Parse(content);

        Assert.Equal("O arquivo excede o limite de 2 MB.", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("Senha")]
    [InlineData("PIN")]
    [InlineData("Template Facial")]
    [InlineData("Impressão digital")]
    public void Parse_ShouldRejectCredentialAndBiometricColumns(string restrictedHeader)
    {
        var csv = $"Bloco;Unidade;Nome;{restrictedHeader}\nA;101;Ana;segredo";

        var result = _parser.Parse(csv);

        Assert.Contains(result.Errors, error => error.Contains("dado restrito", StringComparison.Ordinal));
        Assert.Empty(result.Rows);
    }
}

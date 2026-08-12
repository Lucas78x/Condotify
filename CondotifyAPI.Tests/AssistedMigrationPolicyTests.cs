using CondotifyAPI.Data.Imports;
using CondotifyAPI.Services.Imports;

namespace CondotifyAPI.Tests;

public sealed class AssistedMigrationPolicyTests
{
    [Fact]
    public void Validate_AcceptsDocumentedControllerInstructions()
    {
        var input = ValidInput();

        var errors = AssistedMigrationPolicy.Validate(input);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsUnconfirmedAuthorizationAndRestrictedDataDeclaration()
    {
        var input = ValidInput();
        input.ControllerAuthorizationConfirmed = false;
        input.NoRestrictedDataConfirmed = false;

        var errors = AssistedMigrationPolicy.Validate(input);

        Assert.Contains(errors, error => error.Contains("autorizado", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("biométrico", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("backup.bak")]
    [InlineData("exportacao.zip")]
    [InlineData("banco.mdb")]
    public void Validate_RejectsOpaqueOrDatabaseFiles(string fileName)
    {
        var input = ValidInput();
        input.FileName = fileName;

        var error = Assert.Single(AssistedMigrationPolicy.Validate(input));

        Assert.Contains("CSV ou TXT", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FileSha256_IsStableWithoutPersistingTheContents()
    {
        var first = AssistedMigrationPolicy.FileSha256("Bloco;Unidade\nA;101");
        var second = AssistedMigrationPolicy.FileSha256("Bloco;Unidade\nA;101");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain("101", first, StringComparison.Ordinal);
    }

    private static StructureImportIn ValidInput() => new()
    {
        FileName = "moradores.csv",
        SourceSystem = "iVMS4200",
        ProcessingBasis = "Contract",
        AuthorizedBy = "Síndico responsável",
        AuthorizationReference = "ATA-2026-08",
        ControllerAuthorizationConfirmed = true,
        PurposeLimitationConfirmed = true,
        NoRestrictedDataConfirmed = true
    };
}

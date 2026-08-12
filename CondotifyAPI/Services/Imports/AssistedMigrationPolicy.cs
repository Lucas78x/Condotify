using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Data.Imports;

namespace CondotifyAPI.Services.Imports;

/// <summary>
/// Keeps the assisted importer inside its declared technical and legal boundary.
/// It validates the controller's instructions; it does not decide the lawful basis
/// on the controller's behalf and never accepts credentials or biometric material.
/// </summary>
public static class AssistedMigrationPolicy
{
    private static readonly IReadOnlyDictionary<string, string> Sources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Situator"] = "Situator",
            ["Condfy"] = "Condfy",
            ["HikCentral"] = "HikCentral",
            ["iVMS4200"] = "iVMS-4200",
            ["InControl"] = "InControl",
            ["CondoMOB"] = "CondoMOB",
            ["Other"] = "Outro sistema ou planilha"
        };

    private static readonly IReadOnlyDictionary<string, string> Bases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Contract"] = "Execução de contrato ou procedimentos preliminares",
            ["LegalObligation"] = "Cumprimento de obrigação legal ou regulatória",
            ["LegitimateInterest"] = "Legítimo interesse documentado",
            ["RegularExercise"] = "Exercício regular de direitos",
            ["Consent"] = "Consentimento documentado"
        };

    public static IReadOnlyList<string> Validate(StructureImportIn input)
    {
        var errors = new List<string>();
        if (!Sources.ContainsKey(input.SourceSystem?.Trim() ?? string.Empty))
            errors.Add("Selecione o sistema ou formato de origem dos dados.");
        if (!Bases.ContainsKey(input.ProcessingBasis?.Trim() ?? string.Empty))
            errors.Add("Informe a base de tratamento definida pelo controlador.");
        ValidateRequired(input.AuthorizedBy, 150, "Informe quem autorizou a migração em nome do controlador.", errors);
        ValidateRequired(input.AuthorizationReference, 180, "Informe a referência da autorização, como chamado, ata ou contrato.", errors);
        if (!input.ControllerAuthorizationConfirmed)
            errors.Add("Confirme que o controlador declarou estar autorizado a fornecer e migrar os dados.");
        if (!input.PurposeLimitationConfirmed)
            errors.Add("Confirme que os dados serão usados somente para implantação e continuidade operacional.");
        if (!input.NoRestrictedDataConfirmed)
            errors.Add("Confirme que o arquivo não contém senhas, PINs, digitais ou material biométrico.");

        var extension = Path.GetExtension(Path.GetFileName(input.FileName ?? string.Empty));
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            errors.Add("Nesta etapa, envie somente um arquivo CSV ou TXT exportado ou preparado pelo condomínio.");
        return errors;
    }

    public static string SourceLabel(string source) =>
        Sources.TryGetValue(source?.Trim() ?? string.Empty, out var label) ? label : "Origem não reconhecida";

    public static string BasisLabel(string basis) =>
        Bases.TryGetValue(basis?.Trim() ?? string.Empty, out var label) ? label : "Base não reconhecida";

    public static string FileSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
        try { return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void ValidateRequired(string? value, int maxLength, string message, ICollection<string> errors)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < 3) errors.Add(message);
        else if (normalized.Length > maxLength) errors.Add($"O campo informado excede {maxLength} caracteres.");
    }
}

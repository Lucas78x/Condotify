using Condotify.Models;
using CondotifyAPI.Domain.DTO.Operations;

namespace CondotifyAPI.Services.Operations;

public interface IDigitalPassProviderService
{
    Task<DigitalPassViewModel> BuildAsync(DigitalPassDTO pass, string token, string publicUrl, CancellationToken cancellationToken = default);
}

public sealed class DigitalPassProviderService(
    IWalletIntegrationStore integrationStore,
    IGoogleWalletJwtSigner signer) : IDigitalPassProviderService
{
    public async Task<DigitalPassViewModel> BuildAsync(DigitalPassDTO pass, string token, string publicUrl, CancellationToken cancellationToken = default)
    {
        var visit = pass.Visit;
        var host = visit.HostResident;
        var unitLabel = host?.Unit is null
            ? string.Empty
            : $"{host.Unit.Block?.Name} / {host.Unit.Number}".Trim(' ', '/');
        var output = new DigitalPassViewModel
        {
            Id = pass.Id, VisitId = pass.VisitId, Status = pass.Status.ToString(),
            VisitorName = visit.VisitorName, HostName = host?.Name ?? string.Empty,
            LicenseName = pass.License?.Name ?? string.Empty, UnitLabel = unitLabel,
            Purpose = visit.Purpose, CredentialCode = visit.Credential?.Identifier ?? string.Empty,
            PublicUrl = publicUrl, ValidFrom = visit.ValidFrom, ValidTo = visit.ValidTo,
            IssuedAt = pass.IssuedAt
        };
        output.GoogleWalletUrl = await BuildGoogleWalletUrlAsync(output, pass.License?.EnterpriseId ?? Guid.Empty, cancellationToken);
        output.GoogleWalletConfigured = !string.IsNullOrWhiteSpace(output.GoogleWalletUrl);
        return output;
    }

    private async Task<string> BuildGoogleWalletUrlAsync(DigitalPassViewModel pass, Guid enterpriseId, CancellationToken cancellationToken)
    {
        var settings = await integrationStore.GetGoogleAsync(enterpriseId, cancellationToken);
        if (settings is null) return string.Empty;

        try
        {
            var objectId = $"{settings.IssuerId}.{pass.Id:N}";
            var genericObject = new Dictionary<string, object>
            {
                ["id"] = objectId,
                ["classId"] = $"{settings.IssuerId}.{settings.ClassSuffix}",
                ["state"] = "ACTIVE",
                ["cardTitle"] = Localized("Condotify"),
                ["header"] = Localized($"Acesso de {pass.VisitorName}"),
                ["subheader"] = Localized(pass.LicenseName),
                ["hexBackgroundColor"] = "#173E9C",
                ["barcode"] = new Dictionary<string, object>
                {
                    ["type"] = "QR_CODE", ["value"] = pass.CredentialCode,
                    ["alternateText"] = pass.CredentialCode
                },
                ["validTimeInterval"] = new Dictionary<string, object>
                {
                    ["start"] = new Dictionary<string, object> { ["date"] = pass.ValidFrom.ToUniversalTime().ToString("O") },
                    ["end"] = new Dictionary<string, object> { ["date"] = pass.ValidTo.ToUniversalTime().ToString("O") }
                },
                ["textModulesData"] = new object[]
                {
                    new Dictionary<string, object> { ["id"] = "host", ["header"] = "ANFITRIAO", ["body"] = pass.HostName },
                    new Dictionary<string, object> { ["id"] = "unit", ["header"] = "DESTINO", ["body"] = pass.UnitLabel },
                    new Dictionary<string, object> { ["id"] = "purpose", ["header"] = "MOTIVO", ["body"] = pass.Purpose }
                },
                ["linksModuleData"] = new Dictionary<string, object>
                {
                    ["uris"] = new object[] { new Dictionary<string, object> { ["uri"] = pass.PublicUrl, ["description"] = "Abrir passe no Condotify", ["id"] = "condotify" } }
                }
            };
            var payload = new Dictionary<string, object>
            {
                ["iss"] = settings.ServiceAccountEmail,
                ["aud"] = "google",
                ["typ"] = "savetowallet",
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["origins"] = Array.Empty<string>(),
                ["payload"] = new Dictionary<string, object> { ["genericObjects"] = new object[] { genericObject } }
            };
            var jwt = await signer.SignAsync(payload, settings, cancellationToken);
            return $"https://pay.google.com/gp/v/save/{jwt}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, object> Localized(string value) => new()
    {
        ["defaultValue"] = new Dictionary<string, object> { ["language"] = "pt-BR", ["value"] = value }
    };

    internal static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    internal static string ResolvePublicUrl(IConfiguration configuration, string requestHostRoot, string token)
    {
        var root = FirstNonBlank(
            configuration["DigitalPass:PublicAppUrl"],
            Environment.GetEnvironmentVariable("CONDOTIFY_PUBLIC_APP_URL"),
            requestHostRoot);
        return $"{root!.TrimEnd('/')}/passe/{Uri.EscapeDataString(token)}";
    }
}

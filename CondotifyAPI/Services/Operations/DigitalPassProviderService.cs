using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Operations;
using Microsoft.IdentityModel.Tokens;

namespace CondotifyAPI.Services.Operations;

public interface IDigitalPassProviderService
{
    DigitalPassViewModel Build(DigitalPassDTO pass, string token, string publicUrl);
}

public sealed class DigitalPassProviderService(IConfiguration configuration) : IDigitalPassProviderService
{
    public DigitalPassViewModel Build(DigitalPassDTO pass, string token, string publicUrl)
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
        output.GoogleWalletUrl = BuildGoogleWalletUrl(output);
        output.GoogleWalletConfigured = !string.IsNullOrWhiteSpace(output.GoogleWalletUrl);
        var appleTemplate = configuration["DigitalPass:AppleWallet:PassServiceUrlTemplate"]
                            ?? Environment.GetEnvironmentVariable("CONDOTIFY_APPLE_WALLET_URL_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(appleTemplate))
        {
            output.AppleWalletUrl = appleTemplate.Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
            output.AppleWalletConfigured = true;
        }
        return output;
    }

    private string BuildGoogleWalletUrl(DigitalPassViewModel pass)
    {
        var issuerId = configuration["DigitalPass:GoogleWallet:IssuerId"] ?? Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_ISSUER_ID");
        var serviceAccount = configuration["DigitalPass:GoogleWallet:ServiceAccountEmail"] ?? Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_SERVICE_ACCOUNT_EMAIL");
        var privateKey = configuration["DigitalPass:GoogleWallet:PrivateKey"] ?? Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_PRIVATE_KEY");
        var classSuffix = configuration["DigitalPass:GoogleWallet:ClassSuffix"] ?? "condotify_access";
        if (string.IsNullOrWhiteSpace(issuerId) || string.IsNullOrWhiteSpace(serviceAccount) || string.IsNullOrWhiteSpace(privateKey)) return string.Empty;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey.Replace("\\n", "\n", StringComparison.Ordinal));
            var header = new JwtHeader(new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
            var objectId = $"{issuerId}.{pass.Id:N}";
            var genericObject = new Dictionary<string, object>
            {
                ["id"] = objectId,
                ["classId"] = $"{issuerId}.{classSuffix}",
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
            var payload = new JwtPayload
            {
                ["iss"] = serviceAccount,
                ["aud"] = "google",
                ["typ"] = "savetowallet",
                ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["origins"] = Array.Empty<string>(),
                ["payload"] = new Dictionary<string, object> { ["genericObjects"] = new object[] { genericObject } }
            };
            var jwt = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
            return $"https://pay.google.com/gp/v/save/{jwt}";
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, object> Localized(string value) => new()
    {
        ["defaultValue"] = new Dictionary<string, object> { ["language"] = "pt-BR", ["value"] = value }
    };
}

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.Operations;
using Google.Apis.Auth.OAuth2;
using Microsoft.IdentityModel.Tokens;

namespace CondotifyAPI.Services.Operations;

public interface IGoogleWalletJwtSigner
{
    Task<string> SignAsync(IReadOnlyDictionary<string, object> payload, GoogleWalletSettings settings, CancellationToken cancellationToken = default);
}

public sealed class GoogleWalletJwtSigner(IHttpClientFactory httpClientFactory) : IGoogleWalletJwtSigner
{
    private const string CloudScope = "https://www.googleapis.com/auth/cloud-platform";

    public Task<string> SignAsync(IReadOnlyDictionary<string, object> payload, GoogleWalletSettings settings, CancellationToken cancellationToken = default) =>
        settings.AuthenticationMode switch
        {
            WalletAuthenticationModeEnum.PrivateKey => Task.FromResult(SignLocally(payload, settings.PrivateKey)),
            WalletAuthenticationModeEnum.ManagedIdentity => SignWithGoogleAsync(payload, settings.ServiceAccountEmail, cancellationToken),
            _ => throw new InvalidOperationException("Modo de autenticacao do Google Wallet nao suportado.")
        };

    private static string SignLocally(IReadOnlyDictionary<string, object> payload, string privateKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey.Replace("\\n", "\n", StringComparison.Ordinal));
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var jwtPayload = new JwtPayload();
        foreach (var item in payload) jwtPayload[item.Key] = item.Value;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(new JwtHeader(credentials), jwtPayload));
    }

    private async Task<string> SignWithGoogleAsync(
        IReadOnlyDictionary<string, object> payload,
        string serviceAccountEmail,
        CancellationToken cancellationToken)
    {
        GoogleCredential credential;
        try
        {
            credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken);
            if (credential.IsCreateScopedRequired) credential = credential.CreateScoped(CloudScope);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("A identidade gerenciada do servidor nao esta disponivel para o Google Wallet.", exception);
        }

        var accessToken = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/{Uri.EscapeDataString(serviceAccountEmail)}:signJwt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { payload = JsonSerializer.Serialize(payload) }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"O Google recusou a assinatura gerenciada ({(int)response.StatusCode}). Verifique a permissao iam.serviceAccounts.signJwt.");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("signedJwt", out var token) && !string.IsNullOrWhiteSpace(token.GetString())
            ? token.GetString()!
            : throw new InvalidOperationException("O Google nao retornou o JWT assinado.");
    }
}

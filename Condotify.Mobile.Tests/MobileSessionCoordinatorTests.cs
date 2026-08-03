using System.Net;
using System.Text;
using System.Text.Json;
using Condotify.Mobile.Core;

namespace Condotify.Mobile.Tests;

public sealed class MobileSessionCoordinatorTests
{
    [Fact]
    public async Task LoginStaff_PersistsStaffSessionWithoutPassword()
    {
        var vault = new MemoryVault();
        var handler = new RecordingHandler(request => Json(new
        {
            result = "Success",
            accessToken = Jwt(DateTimeOffset.UtcNow.AddHours(1), name: "Operador"),
            refreshToken = "refresh-1",
            expiresIn = 3600
        }));
        var service = Create(handler, vault);

        var result = await service.LoginStaffAsync("staff@condotify.local", "secret", "Android");

        Assert.True(result.Success);
        Assert.Equal(MobilePrincipalKind.Staff, service.Current?.Principal);
        Assert.Equal("Operador", service.Current?.Name);
        Assert.NotNull(vault.Value);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(vault.Value));
        Assert.Equal("/api/auth/login", handler.Paths.Single());
    }

    [Fact]
    public async Task LoginResident_UsesResidentEndpointAndKeepsLicenseScope()
    {
        var licenseId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => Json(new
        {
            result = "Success",
            accessToken = Jwt(DateTimeOffset.UtcNow.AddHours(1), residentId, licenseId: licenseId),
            refreshToken = "resident-refresh",
            expiresIn = 3600,
            name = "Lucas",
            email = "lucas@example.com",
            residentId,
            licenseId,
            licenseName = "Residencial Azul"
        }));
        var service = Create(handler, new MemoryVault());

        var result = await service.LoginResidentAsync("lucas@example.com", "secret", "iPhone");

        Assert.True(result.Success);
        Assert.Equal(MobilePrincipalKind.Resident, service.Current?.Principal);
        Assert.Equal(licenseId, service.Current?.LicenseId);
        Assert.Equal("/api/auth/resident/login", handler.Paths.Single());
    }

    [Fact]
    public async Task ForgotPassword_PostsToResidentForgotEndpoint()
    {
        var handler = new RecordingHandler(_ => Json(new { result = "Accepted" }, HttpStatusCode.Accepted));
        var service = Create(handler, new MemoryVault());

        await service.ForgotPasswordAsync("lucas@example.com");

        Assert.Equal("/api/auth/resident/password/forgot", handler.Paths.Single());
    }

    [Fact]
    public async Task ResetPassword_ReturnsSuccessWhenBackendAccepts()
    {
        var handler = new RecordingHandler(_ => Json(new { result = "Success" }));
        var service = Create(handler, new MemoryVault());

        var result = await service.ResetPasswordAsync("recovery-token", "NovaSenha123!");

        Assert.True(result.Success);
        Assert.Equal("/api/auth/resident/password/reset", handler.Paths.Single());
    }

    [Fact]
    public async Task ResetPassword_ReturnsBackendErrorWhenTokenInvalid()
    {
        var handler = new RecordingHandler(_ => Json(
            new { result = "InvalidToken", error = "Codigo de recuperacao invalido ou expirado." },
            HttpStatusCode.BadRequest));
        var service = Create(handler, new MemoryVault());

        var result = await service.ResetPasswordAsync("bad-token", "NovaSenha123!");

        Assert.False(result.Success);
        Assert.Equal("Codigo de recuperacao invalido ou expirado.", result.Error);
    }

    [Fact]
    public async Task ConcurrentTokenReads_RotateRefreshTokenOnlyOnce()
    {
        var refreshCalls = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh"))
            {
                Interlocked.Increment(ref refreshCalls);
                return Json(new
                {
                    result = "Success",
                    accessToken = Jwt(DateTimeOffset.UtcNow.AddHours(1)),
                    refreshToken = "refresh-rotated",
                    expiresIn = 3600
                });
            }

            return Json(new
            {
                result = "Success",
                accessToken = Jwt(DateTimeOffset.UtcNow.AddSeconds(5)),
                refreshToken = "refresh-original",
                expiresIn = 5
            });
        });
        var service = Create(handler, new MemoryVault());
        Assert.True((await service.LoginStaffAsync("a@b.com", "secret", "test")).Success);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(async _ => await service.GetAccessTokenAsync()));

        Assert.Equal(1, refreshCalls);
        Assert.All(tokens, token => Assert.NotNull(token));
        Assert.Equal("refresh-rotated", service.Current?.RefreshToken);
    }

    [Fact]
    public async Task Logout_ClearsLocalVaultWhenNetworkFails()
    {
        var vault = new MemoryVault();
        var loginCompleted = false;
        var handler = new RecordingHandler(_ =>
        {
            if (loginCompleted) throw new HttpRequestException("offline");
            loginCompleted = true;
            return Json(new
            {
                result = "Success",
                accessToken = Jwt(DateTimeOffset.UtcNow.AddHours(1)),
                refreshToken = "refresh",
                expiresIn = 3600
            });
        });
        var service = Create(handler, vault);
        await service.LoginStaffAsync("a@b.com", "secret", "test");

        await service.LogoutAsync();

        Assert.Null(service.Current);
        Assert.Null(vault.Value);
        Assert.True(vault.Cleared);
    }

    [Theory]
    [InlineData("https://evil.example/app/home")]
    [InlineData("/admin")]
    [InlineData("/visitors/../../admin")]
    public void DeepLinkRouter_RejectsRoutesOutsideAllowlist(string value) =>
        Assert.False(MobileNavigation.TryResolveDeepLink(value, out _));

    [Fact]
    public void Navigation_IsSeparatedByPrincipal()
    {
        var staff = MobileNavigation.For(MobilePrincipalKind.Staff);
        var resident = MobileNavigation.For(MobilePrincipalKind.Resident);

        Assert.Contains(staff, x => x.Route == "/concierge");
        Assert.DoesNotContain(resident, x => x.Route == "/concierge");
        Assert.Contains(resident, x => x.Route == "/visitors");
    }

    private static MobileSessionCoordinator Create(RecordingHandler handler, MemoryVault vault)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        return new MobileSessionCoordinator(new SingleClientFactory(client), vault);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static string Jwt(
        DateTimeOffset expires,
        Guid? subjectId = null,
        string name = "Usuario",
        Guid? licenseId = null)
    {
        static string Part(object value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part(new { alg = "none" })}.{Part(new { sub = (subjectId ?? Guid.NewGuid()).ToString(), name, license_id = licenseId, exp = expires.ToUnixTimeSeconds() })}.signature";
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(response(request));
        }
    }

    private sealed class MemoryVault : IMobileSessionVault
    {
        public MobileSession? Value { get; private set; }
        public bool Cleared { get; private set; }
        public Task<MobileSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(MobileSession session, CancellationToken cancellationToken = default)
        {
            Value = session;
            Cleared = false;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            Cleared = true;
            return Task.CompletedTask;
        }
    }
}

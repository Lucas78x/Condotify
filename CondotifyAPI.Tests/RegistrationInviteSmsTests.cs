using System.Net;
using System.Text;
using CondotifyAPI.Services.Invitation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CondotifyAPI.Tests;

public sealed class RegistrationInviteSmsTests
{
    [Theory]
    [InlineData("(77) 99999-9999", "55", "+5577999999999")]
    [InlineData("+1 415 555 2671", "55", "+14155552671")]
    [InlineData("005577999999999", "55", "+5577999999999")]
    public void PhoneNormalization_ShouldProduceE164(
        string input,
        string countryCode,
        string expected)
    {
        var valid = RegistrationInviteSmsSender.TryNormalizePhone(
            input,
            countryCode,
            out var normalized);

        Assert.True(valid);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("telefone 77 99999-9999")]
    [InlineData("123")]
    [InlineData("+0123456789")]
    public void PhoneNormalization_ShouldRejectInvalidValues(string input)
    {
        Assert.False(RegistrationInviteSmsSender.TryNormalizePhone(input, "55", out _));
    }

    [Fact]
    public async Task Sender_ShouldAuthenticateWithApiKeyAndPostExpectedMessage()
    {
        const string accountSid = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string apiKeySid = "SKbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string apiKeySecret = "dummy-test-secret";
        var handler = new CaptureHandler();
        var sender = CreateSender(handler, new Dictionary<string, string?>
        {
            ["Twilio:AccountSid"] = accountSid,
            ["Twilio:ApiKeySid"] = apiKeySid,
            ["Twilio:ApiKeySecret"] = apiKeySecret,
            ["Twilio:FromNumber"] = "+15005550006",
            ["Twilio:DefaultCountryCode"] = "55"
        });

        var result = await sender.SendAsync(
            "(77) 99999-9999",
            "Lucas",
            "Condomínio Teste",
            "https://fefaccess.grupoff.net.br/cadastro/convite/abc123",
            new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(result.Success);
        Assert.Equal("SMcccccccccccccccccccccccccccccccc", result.ProviderMessageId);
        Assert.Equal(
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json",
            handler.RequestUri?.ToString());
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Equal(
            $"{apiKeySid}:{apiKeySecret}",
            Encoding.ASCII.GetString(Convert.FromBase64String(handler.AuthorizationParameter!)));
        Assert.Equal("+5577999999999", handler.Form["To"]);
        Assert.Equal("+15005550006", handler.Form["From"]);
        Assert.Contains("https://fefaccess.grupoff.net.br/cadastro/convite/abc123", handler.Form["Body"]);
    }

    [Fact]
    public async Task Sender_ShouldPreferMessagingServiceOverFromNumber()
    {
        var handler = new CaptureHandler();
        var sender = CreateSender(handler, new Dictionary<string, string?>
        {
            ["Twilio:AccountSid"] = "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ["Twilio:ApiKeySid"] = "SKbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ["Twilio:ApiKeySecret"] = "dummy-test-secret",
            ["Twilio:MessagingServiceSid"] = "MGdddddddddddddddddddddddddddddddd"
        });

        var result = await sender.SendAsync(
            "+5577999999999",
            "Lucas",
            "Condomínio Teste",
            "https://example.test/cadastro/convite/token",
            DateTime.UtcNow.AddDays(7));

        Assert.True(result.Success);
        Assert.Equal("MGdddddddddddddddddddddddddddddddd", handler.Form["MessagingServiceSid"]);
        Assert.False(handler.Form.ContainsKey("From"));
    }

    [Fact]
    public void Mask_ShouldExposeOnlyLastFourDigits()
    {
        Assert.Equal("***9999", RegistrationInviteSmsSender.Mask("+55 77 99999-9999"));
    }

    private static RegistrationInviteSmsSender CreateSender(
        HttpMessageHandler handler,
        IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new RegistrationInviteSmsSender(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.twilio.com/") },
            configuration,
            NullLogger<RegistrationInviteSmsSender>.Instance);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public Dictionary<string, string> Form { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var item in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"sid\":\"SMcccccccccccccccccccccccccccccccc\",\"status\":\"queued\"}")
            };
        }
    }
}

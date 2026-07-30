using System.Net;
using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Services.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CondotifyAPI.Tests;

public sealed class AlertNotificationTests
{
    [Fact]
    public void Signature_ShouldUseHmacSha256WithStablePrefix()
    {
        const string payload = """{"event":"alert.opened"}""";
        const string secret = "notification-test-secret";
        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        var signature = AlertNotificationChannelSender.Sign(payload, secret);

        Assert.Equal($"sha256={expected}", signature);
    }

    [Fact]
    public void Recipients_ShouldIgnoreInvalidAndNormalizeSupportedSeparators()
    {
        var recipients = AlertNotificationChannelSender.ParseRecipients(
            "operacao@empresa.com;invalid-address,\nsuporte@empresa.com");

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, x => x.Address == "operacao@empresa.com");
        Assert.Contains(recipients, x => x.Address == "suporte@empresa.com");
    }

    [Fact]
    public async Task Webhook_ShouldSendSignedPayloadAndDeliveryIdentity()
    {
        var handler = new CaptureHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var sender = CreateSender(handler);
        var policy = new AlertNotificationPolicyDTO
        {
            WebhookUrl = "https://hooks.example.test/condotify",
            WebhookSecret = "strong-secret"
        };
        var alertId = Guid.NewGuid();

        var result = await sender.SendAsync(
            NotificationChannel.Webhook,
            policy,
            Message(alertId, 2));

        Assert.True(result.Success);
        Assert.Equal(202, result.ResponseCode);
        Assert.NotNull(handler.Request);
        Assert.Equal("alert.escalated", Assert.Single(handler.Request!.Headers.GetValues("X-Condotify-Event")));
        Assert.Equal($"{alertId:N}:2", Assert.Single(handler.Request.Headers.GetValues("X-Condotify-Delivery")));
        Assert.StartsWith("sha256=", Assert.Single(handler.Request.Headers.GetValues("X-Condotify-Signature")));
        Assert.Contains(alertId.ToString(), handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Critical", 14, 0)]
    [InlineData("Critical", 15, 1)]
    [InlineData("Critical", 75, 2)]
    [InlineData("Warning", 59, 0)]
    [InlineData("Warning", 60, 1)]
    public void Escalation_ShouldRespectSeveritySlaAndRepeat(
        string severity,
        int elapsedMinutes,
        int expectedLevel)
    {
        var now = new DateTime(2026, 7, 30, 15, 0, 0, DateTimeKind.Utc);
        var alert = new OperationalAlertDTO
        {
            Severity = Enum.Parse<OperationalAlertSeverity>(severity),
            FirstOccurredAt = now.AddMinutes(-elapsedMinutes)
        };
        var policy = new AlertNotificationPolicyDTO
        {
            CriticalSlaMinutes = 15,
            WarningSlaMinutes = 60,
            EscalationRepeatMinutes = 60
        };

        var level = AlertNotificationWorker.EscalationLevel(alert, policy, now);

        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public void DestinationLabels_ShouldNotExposeQueryTokensOrEmailAddresses()
    {
        var endpoint = AlertNotificationWorker.EndpointLabel(
            "https://hooks.example.test/notify?token=sensitive");
        var recipients = AlertNotificationWorker.RecipientLabel(
            "one@example.com;two@example.com");

        Assert.Equal("https://hooks.example.test/•••", endpoint);
        Assert.DoesNotContain("sensitive", endpoint);
        Assert.DoesNotContain("notify", endpoint);
        Assert.Equal("2 destinatários", recipients);
    }

    [Fact]
    public void Smtp_ShouldPreferWebSettingsAndKeepEnvironmentAsFallback()
    {
        var environmentSender = CreateSender(
            new CaptureHandler(new HttpResponseMessage(HttpStatusCode.OK)),
            new Dictionary<string, string?>
            {
                ["Notifications:Smtp:Host"] = "smtp.environment.test",
                ["Notifications:Smtp:Port"] = "587",
                ["Notifications:Smtp:FromEmail"] = "environment@example.test"
            });
        var webPolicy = new AlertNotificationPolicyDTO
        {
            SmtpHost = "smtp.web.test",
            SmtpPort = 465,
            SmtpFromEmail = "web@example.test"
        };

        Assert.True(environmentSender.IsEmailTransportReady(new AlertNotificationPolicyDTO()));
        Assert.Equal("Environment", environmentSender.EmailTransportSource(new AlertNotificationPolicyDTO()));
        Assert.True(environmentSender.IsEmailTransportReady(webPolicy));
        Assert.Equal("Web", environmentSender.EmailTransportSource(webPolicy));
    }

    private static AlertNotificationChannelSender CreateSender(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        return new AlertNotificationChannelSender(
            new StubHttpClientFactory(client),
            configuration,
            NullLogger<AlertNotificationChannelSender>.Instance);
    }

    private static AlertNotificationMessage Message(Guid alertId, int level) => new(
        alertId,
        Guid.NewGuid(),
        "Condomínio Teste",
        "DeviceOffline",
        "Critical",
        "Portaria offline",
        "O equipamento não respondeu.",
        "Open",
        level,
        DateTime.UtcNow,
        "/operacoes");

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}

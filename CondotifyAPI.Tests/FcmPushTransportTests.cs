using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Services.Mobile;

namespace CondotifyAPI.Tests;

public sealed class FcmPushTransportTests
{
    [Fact]
    public void Classify_Success_ReturnsProviderMessageId()
    {
        var result = FcmPushTransport.Classify(HttpStatusCode.OK, """{"name":"projects/demo/messages/123"}""");

        Assert.Equal(PushTransportOutcome.Delivered, result.Outcome);
        Assert.Equal("projects/demo/messages/123", result.ProviderMessageId);
    }

    [Fact]
    public void Classify_UnregisteredToken_MarksInstallationInvalid()
    {
        const string body = """
            {"error":{"code":404,"message":"Requested entity was not found.","details":[{"@type":"type.googleapis.com/google.firebase.fcm.v1.FcmError","errorCode":"UNREGISTERED"}]}}
            """;

        var result = FcmPushTransport.Classify(HttpStatusCode.NotFound, body);

        Assert.Equal(PushTransportOutcome.InvalidToken, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Classify_RetryableStatus_ReturnsTransientFailure(HttpStatusCode status)
    {
        var result = FcmPushTransport.Classify(status, "{}");

        Assert.Equal(PushTransportOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public void Classify_InvalidPayload_DoesNotMistakeItForAnInvalidToken()
    {
        const string body = """{"error":{"code":400,"message":"Invalid payload"}}""";

        var result = FcmPushTransport.Classify(HttpStatusCode.BadRequest, body);

        Assert.Equal(PushTransportOutcome.PermanentFailure, result.Outcome);
    }

    [Fact]
    public void BuildPayload_ContainsOnlyExpectedNavigationAndStringData()
    {
        var payload = FcmPushTransport.BuildPayload("sensitive-fcm-token", new PushTransportMessage(
            "Visitante aguardando",
            "Há uma nova solicitação.",
            "/visitors/11111111-2222-3333-4444-555555555555",
            "https://app.condotify.com.br/app/visitors/11111111-2222-3333-4444-555555555555",
            "Visitor",
            new Dictionary<string, string> { ["notificationId"] = "abc" }));

        var json = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(json);
        var message = document.RootElement.GetProperty("message");
        Assert.Equal("sensitive-fcm-token", message.GetProperty("token").GetString());
        Assert.False(message.TryGetProperty("notification", out _));
        Assert.Equal("Visitante aguardando", message.GetProperty("data").GetProperty("title").GetString());
        Assert.Equal("Há uma nova solicitação.", message.GetProperty("data").GetProperty("body").GetString());
        Assert.Equal("Visitor", message.GetProperty("data").GetProperty("category").GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
        var aps = message.GetProperty("apns").GetProperty("payload").GetProperty("aps");
        Assert.Equal("Visitante aguardando", aps.GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("default", aps.GetProperty("sound").GetString());
    }

    [Fact]
    public void CreateAssertion_ProducesAValidShortLivedRs256Jwt()
    {
        using var rsa = RSA.Create(2048);
        var account = new FcmServiceAccount(
            "project-id",
            "service-account@example.iam.gserviceaccount.com",
            rsa.ExportPkcs8PrivateKeyPem(),
            "https://oauth2.googleapis.com/token");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        var assertion = FcmAccessTokenProvider.CreateAssertion(account, now);
        var parts = assertion.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.True(rsa.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Decode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal(account.ClientEmail, payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(now.ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now.AddMinutes(55).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
    }

    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    }
}

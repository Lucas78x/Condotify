using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Services.Invitation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CondotifyAPI.Tests;

public sealed class RegistrationInviteEmailTests
{
    [Fact]
    public void ChannelContract_ShouldRemainAlignedWithPortalValues()
    {
        Assert.Equal(1, (int)RegistrationInviteChannelEnum.Email);
        Assert.Equal(2, (int)RegistrationInviteChannelEnum.Sms);
        Assert.Equal(3, (int)RegistrationInviteChannelEnum.WhatsApp);
        Assert.Equal(4, (int)RegistrationInviteChannelEnum.Link);
    }

    [Theory]
    [InlineData("morador@example.com", true)]
    [InlineData("  morador@example.com  ", true)]
    [InlineData("Morador <morador@example.com>", false)]
    [InlineData("telefone-11999999999", false)]
    [InlineData("", false)]
    public void EmailValidation_ShouldAcceptOnlyPlainMailbox(string value, bool expected)
    {
        Assert.Equal(expected, PeopleManagementController.IsValidEmail(value));
    }

    [Fact]
    public void Html_ShouldEscapeUserDataAndIncludeSecureCallToAction()
    {
        var expiresAt = new DateTime(2026, 8, 29, 20, 30, 0, DateTimeKind.Utc);
        const string inviteUrl = "https://fefaccess.grupoff.net.br/cadastro/convite/abc123?source=email&safe=true";

        var html = RegistrationInviteEmailSender.BuildHtmlBody(
            "Lucas <script>alert(1)</script>",
            "Condomínio A & B",
            inviteUrl,
            expiresAt);

        Assert.Contains("Completar meu cadastro", html);
        Assert.Contains("https://fefaccess.grupoff.net.br/cadastro/convite/abc123?source=email&amp;safe=true", html);
        Assert.Contains("A &amp; B", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlainText_ShouldRemainUsefulWhenHtmlIsUnavailable()
    {
        var body = RegistrationInviteEmailSender.BuildPlainText(
            "Lucas",
            "Grupo F&F",
            "https://fefaccess.grupoff.net.br/cadastro/convite/abc123",
            new DateTime(2026, 8, 29, 20, 30, 0, DateTimeKind.Utc));

        Assert.Contains("Acesse: https://fefaccess.grupoff.net.br/cadastro/convite/abc123", body);
        Assert.Contains("criação da sua senha", body);
        Assert.Contains("Não encaminhe o link", body);
    }

    [Fact]
    public void Sender_ShouldRecognizePerLicenseSmtpWithoutNotificationRecipients()
    {
        var sender = new RegistrationInviteEmailSender(
            new ConfigurationBuilder().Build(),
            NullLogger<RegistrationInviteEmailSender>.Instance);
        var policy = new AlertNotificationPolicyDTO
        {
            SmtpHost = "smtp.example.test",
            SmtpPort = 587,
            SmtpFromEmail = "acesso@example.test",
            SmtpEnableSsl = true,
            EmailEnabled = false,
            EmailRecipients = string.Empty
        };

        Assert.True(sender.IsReady(policy));
    }

    [Fact]
    public void Mask_ShouldNotExposeRecipientLocalPart()
    {
        var masked = RegistrationInviteEmailSender.Mask("lucas.bastos@example.com");

        Assert.Equal("l***@example.com", masked);
        Assert.DoesNotContain("lucas.bastos", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SubjectHeader_ShouldRemoveLineBreaks()
    {
        var subject = RegistrationInviteEmailSender.CleanHeader(
            "Seu acesso ao Condomínio\r\nBcc: attacker@example.com",
            180);

        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
        Assert.Equal("Seu acesso ao Condomínio  Bcc: attacker@example.com", subject);
    }
}

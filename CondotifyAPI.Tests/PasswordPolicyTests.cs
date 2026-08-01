using CondotifyAPI.Services.Security;
using Xunit;

namespace CondotifyAPI.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Abcdef1!")]
    [InlineData("Senha@2026")]
    public void Validate_AcceptsAConformingPassword(string password) =>
        Assert.Null(PasswordPolicy.Validate(password));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Abc1!")]
    public void Validate_RejectsTooShortOrEmpty(string? password) =>
        Assert.NotNull(PasswordPolicy.Validate(password));

    [Theory]
    [InlineData("abcdefg1!")]   // sem maiuscula
    [InlineData("ABCDEFG1!")]   // sem minuscula
    [InlineData("Abcdefgh!")]   // sem digito
    [InlineData("Abcdefg12")]   // sem caractere especial
    public void Validate_RejectsMissingCharacterClasses(string password) =>
        Assert.NotNull(PasswordPolicy.Validate(password));

    [Fact]
    public void Validate_RejectsOverlyLongPassword() =>
        Assert.NotNull(PasswordPolicy.Validate(new string('A', 60) + new string('b', 45) + "1!"));

    [Fact]
    public void Validate_DoesNotEchoThePasswordInItsMessage()
    {
        var message = PasswordPolicy.Validate("segredo");
        Assert.NotNull(message);
        Assert.DoesNotContain("segredo", message);
    }

    [Fact]
    public void Validate_WithoutASubject_DefaultsToNovaSenha_UnchangedFromProduction()
    {
        // AuthController's change-password flow already ships this exact wording;
        // the default must stay byte-for-byte identical.
        Assert.Equal("A nova senha deve ter entre 8 e 100 caracteres.", PasswordPolicy.Validate(null));
    }

    [Fact]
    public void Validate_WithACustomSubject_UsesItInsteadOfNovaSenha()
    {
        var message = PasswordPolicy.Validate(null, "senha");

        Assert.Equal("A senha deve ter entre 8 e 100 caracteres.", message);
        Assert.DoesNotContain("nova", message);
    }
}

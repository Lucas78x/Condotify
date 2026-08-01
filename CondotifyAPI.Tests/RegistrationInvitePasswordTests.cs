using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Covers <see cref="ResidentPasswordSetter"/>, the pure "validate then hash" decision
/// that <c>PublicRegistrationController.Complete</c> calls when a resident sets their
/// first password through a registration invite. There is no EF InMemory/SQLite
/// provider in this test project, so the controller's database path itself is not
/// exercised here; this covers the logic that decides what gets written to
/// resident.Password before any DbContext is involved.
/// </summary>
public class RegistrationInvitePasswordTests
{
    private readonly PasswordHasher<ResidentAccess> _hasher = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MissingPassword_Fails(string? password)
    {
        var result = ResidentPasswordSetter.Resolve(password, _hasher);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Null(result.Hash);
    }

    [Fact]
    public void Resolve_PasswordFailingPolicy_FailsWithThePolicysMessage()
    {
        var result = ResidentPasswordSetter.Resolve("abc", _hasher);

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordPolicy.Validate("abc", "senha"), result.Error);
    }

    [Fact]
    public void Resolve_PasswordFailingPolicy_MessageDoesNotSayNovaSenha()
    {
        // This is the wording problem the task called out: the resident is setting
        // their FIRST password through the invite, not changing an existing one.
        var result = ResidentPasswordSetter.Resolve("abc", _hasher);

        Assert.NotNull(result.Error);
        Assert.DoesNotContain("nova", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ValidPassword_Succeeds()
    {
        var result = ResidentPasswordSetter.Resolve("Abcdef1!", _hasher);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.NotNull(result.Hash);
    }

    [Fact]
    public void Resolve_ValidPassword_TheStoredHashIsNotThePlainPassword()
    {
        // The one test that matters most: what would be written to resident.Password
        // must never be the password itself, in any form.
        const string password = "Abcdef1!";

        var result = ResidentPasswordSetter.Resolve(password, _hasher);

        Assert.NotNull(result.Hash);
        Assert.NotEqual(password, result.Hash);
        Assert.DoesNotContain(password, result.Hash);
    }

    [Fact]
    public void Resolve_ValidPassword_TheHashIsAcceptedByVerifyHashedPassword()
    {
        const string password = "Abcdef1!";

        var result = ResidentPasswordSetter.Resolve(password, _hasher);

        var verification = _hasher.VerifyHashedPassword(null!, result.Hash!, password);
        Assert.NotEqual(PasswordVerificationResult.Failed, verification);
    }

    [Fact]
    public void Resolve_ValidPassword_TheHashRejectsAWrongPassword()
    {
        var result = ResidentPasswordSetter.Resolve("Abcdef1!", _hasher);

        var verification = _hasher.VerifyHashedPassword(null!, result.Hash!, "SomethingElse1!");
        Assert.Equal(PasswordVerificationResult.Failed, verification);
    }
}

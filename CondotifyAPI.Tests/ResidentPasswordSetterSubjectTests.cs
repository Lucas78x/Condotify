using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Task 8 (reset/change password) needs <see cref="ResidentPasswordSetter.Resolve"/>'s exact
/// "validate then hash" behaviour, but the error wording must say "nova senha" - the resident
/// already has a password, unlike the first-access invite flow RegistrationInvitePasswordTests
/// covers (which is deliberately still "senha", the default, unaffected by this new overload).
/// </summary>
public class ResidentPasswordSetterSubjectTests
{
    private readonly PasswordHasher<ResidentAccess> _hasher = new();

    [Fact]
    public void Resolve_DefaultSubject_StillSaysSenha()
    {
        var result = ResidentPasswordSetter.Resolve("abc", _hasher);

        Assert.Equal(PasswordPolicy.Validate("abc", "senha"), result.Error);
    }

    [Fact]
    public void Resolve_NovaSenhaSubject_UsesThatWordingOnFailure()
    {
        var result = ResidentPasswordSetter.Resolve("abc", _hasher, "nova senha");

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordPolicy.Validate("abc", "nova senha"), result.Error);
        Assert.Contains("nova senha", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NovaSenhaSubject_ValidPasswordStillSucceeds()
    {
        var result = ResidentPasswordSetter.Resolve("Abcdef1!", _hasher, "nova senha");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Hash);
    }
}

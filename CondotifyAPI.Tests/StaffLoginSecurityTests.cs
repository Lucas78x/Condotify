using CondotifyAPI.Domain.Models.Users;
using DigitalWorldOnline.Management.Api.Controllers;
using Microsoft.AspNetCore.Identity;

namespace CondotifyAPI.Tests;

public sealed class StaffLoginSecurityTests
{
    private readonly PasswordHasher<UserAccess> _hasher = new();

    [Fact]
    public void UnknownUser_StillPerformsARealHashVerification()
    {
        Assert.False(AuthController.VerifyLoginPassword(null, "wrong-password", _hasher));
    }

    [Fact]
    public void EmptyStoredHash_IsRejectedWithoutSkippingHashVerification()
    {
        Assert.False(AuthController.VerifyLoginPassword(string.Empty, "wrong-password", _hasher));
    }

    [Fact]
    public void ValidAndInvalidPasswords_AreDistinguished()
    {
        var hash = _hasher.HashPassword(null!, "Correct-Password-1!");

        Assert.True(AuthController.VerifyLoginPassword(hash, "Correct-Password-1!", _hasher));
        Assert.False(AuthController.VerifyLoginPassword(hash, "Wrong-Password-1!", _hasher));
    }
}

using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Models.Resident;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Covers task 8's three new pure decisions on <see cref="ResidentAuthController"/> - reset
/// and change share the same "validate the new password, hash it, revoke every refresh token"
/// tail (<see cref="ResidentAuthController.ApplyNewPasswordAsync"/>), current-password
/// verification for "change" (<see cref="ResidentAuthController.VerifyCurrentPassword"/>), and
/// the "never let a failure escape 'forgot'" guard (<see cref="ResidentAuthController.TryRunAsync"/>).
///
/// The property that matters most: <see cref="ApplyNewPasswordAsync"/> only ever calls the
/// revoke-all-tokens delegate when the password change actually took effect - never on a
/// policy-rejected password. This is exercised directly here with a counting fake delegate,
/// since CondotifyAPI.Tests has no EF InMemory/SQLite provider to exercise the real
/// <see cref="CondotifyAPI.Services.Security.IRefreshTokenService.RevokeAllAsync"/> call the
/// controller wires this delegate to.
/// </summary>
public class ResidentAuthControllerPasswordTests
{
    private static readonly PasswordHasher<ResidentAccess> Hasher = new();
    private const string CurrentPassword = "Abcdef1!";
    private static readonly string CurrentPasswordHash = Hasher.HashPassword(null!, CurrentPassword);

    private static ResidentAccessDTO Resident(string? password = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "morador@example.com",
        Password = password ?? CurrentPasswordHash,
        IsActive = true,
        Unit = new UnitDTO { Id = Guid.NewGuid(), Number = "101", Block = new BlockDTO { Id = Guid.NewGuid(), Name = "A" } },
    };

    // --- VerifyCurrentPassword ---------------------------------------------------------

    [Fact]
    public void VerifyCurrentPassword_CorrectPassword_ReturnsTrue()
    {
        Assert.True(ResidentAuthController.VerifyCurrentPassword(CurrentPasswordHash, CurrentPassword, Hasher));
    }

    [Fact]
    public void VerifyCurrentPassword_WrongPassword_ReturnsFalse()
    {
        Assert.False(ResidentAuthController.VerifyCurrentPassword(CurrentPasswordHash, "SomethingElse1!", Hasher));
    }

    [Fact]
    public void VerifyCurrentPassword_EmptyStoredHash_FailsWithoutThrowing()
    {
        var exception = Record.Exception(() => ResidentAuthController.VerifyCurrentPassword("", CurrentPassword, Hasher));

        Assert.Null(exception);
        Assert.False(ResidentAuthController.VerifyCurrentPassword("", CurrentPassword, Hasher));
    }

    // --- ApplyNewPasswordAsync (shared tail of reset AND change) -----------------------

    [Fact]
    public async Task ApplyNewPasswordAsync_ValidPassword_RevokesAllTokensExactlyOnce()
    {
        var resident = Resident();
        var revokeCalls = 0;

        var result = await ResidentAuthController.ApplyNewPasswordAsync(
            resident, "NewPassw0rd!", Hasher, () => { revokeCalls++; return Task.CompletedTask; });

        Assert.True(result.Succeeded);
        Assert.Equal(1, revokeCalls);
    }

    [Fact]
    public async Task ApplyNewPasswordAsync_ValidPassword_StoredValueIsAHashNotThePassword()
    {
        var resident = Resident();
        const string newPassword = "NewPassw0rd!";

        await ResidentAuthController.ApplyNewPasswordAsync(resident, newPassword, Hasher, () => Task.CompletedTask);

        Assert.NotEqual(newPassword, resident.Password);
        Assert.DoesNotContain(newPassword, resident.Password);
        Assert.NotEqual(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(null!, resident.Password, newPassword));
    }

    [Fact]
    public async Task ApplyNewPasswordAsync_PasswordFailingPolicy_DoesNotRevokeTokens()
    {
        var resident = Resident();
        var revokeCalls = 0;

        var result = await ResidentAuthController.ApplyNewPasswordAsync(
            resident, "weak", Hasher, () => { revokeCalls++; return Task.CompletedTask; });

        Assert.False(result.Succeeded);
        Assert.Equal(0, revokeCalls);
    }

    [Fact]
    public async Task ApplyNewPasswordAsync_PasswordFailingPolicy_LeavesStoredPasswordUnchanged()
    {
        var resident = Resident();
        var originalHash = resident.Password;

        await ResidentAuthController.ApplyNewPasswordAsync(resident, "weak", Hasher, () => Task.CompletedTask);

        Assert.Equal(originalHash, resident.Password);
    }

    [Fact]
    public async Task ApplyNewPasswordAsync_PasswordFailingPolicy_ErrorMentionsNovaSenha()
    {
        var resident = Resident();

        var result = await ResidentAuthController.ApplyNewPasswordAsync(resident, "weak", Hasher, () => Task.CompletedTask);

        Assert.NotNull(result.Error);
        Assert.Contains("nova senha", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // --- ApplyPasswordAsync (the primitive ResetPassword calls directly, post-validation) --

    [Fact]
    public async Task ApplyPasswordAsync_SetsTheHashAndRevokesTokensExactlyOnce()
    {
        var resident = Resident();
        var revokeCalls = 0;
        const string hash = "some-precomputed-hash";

        await ResidentAuthController.ApplyPasswordAsync(resident, hash, () => { revokeCalls++; return Task.CompletedTask; });

        Assert.Equal(hash, resident.Password);
        Assert.Equal(1, revokeCalls);
    }

    // --- TryRunAsync (forgot must never let a failure escape) --------------------------

    [Fact]
    public async Task TryRunAsync_ActionThrows_DoesNotPropagate()
    {
        var exception = await Record.ExceptionAsync(() =>
            ResidentAuthController.TryRunAsync(() => throw new InvalidOperationException("boom")));

        Assert.Null(exception);
    }

    [Fact]
    public async Task TryRunAsync_ActionSucceeds_Runs()
    {
        var ran = false;

        await ResidentAuthController.TryRunAsync(() => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }

    // --- ForgotPassword's response body is a fixed constant, never branch-dependent ---

    [Fact]
    public void ForgotPasswordAcceptedBody_ResultIsAccepted()
    {
        Assert.Equal("Accepted", ResidentAuthController.ForgotPasswordAcceptedBody.Result);
    }
}

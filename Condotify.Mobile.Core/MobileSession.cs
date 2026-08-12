namespace Condotify.Mobile.Core;

public enum MobilePrincipalKind
{
    None = 0,
    Staff = 1,
    Resident = 2
}

public sealed record MobileSession(
    MobilePrincipalKind Principal,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    Guid SubjectId,
    string Name,
    string Email,
    Guid? EnterpriseId,
    Guid? LicenseId,
    string LicenseName)
{
    public bool IsAuthenticated =>
        Principal != MobilePrincipalKind.None &&
        SubjectId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(RefreshToken);
}

public sealed record MobileLoginResult(
    bool Success,
    bool MfaRequired,
    string Error,
    string ChallengeToken = "",
    bool CredentialsRejected = false);

public sealed record MobilePasswordResetResult(bool Success, string Error);

public interface IMobileSessionVault
{
    Task<MobileSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MobileSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

namespace CondotifyAPI.Domain.DTO.Resident;

/// <summary>
/// A password-recovery token for "forgot password" (task 8) - the resident equivalent of
/// <c>RefreshTokenDTO</c>/<c>RegistrationInviteDTO</c>: the plaintext value handed to the
/// caller once at issue time is never stored, only <see cref="TokenHash"/> (SHA-256, same
/// scheme as those two). Single-use (<see cref="UsedAt"/>) and short-lived
/// (<see cref="ExpiresAt"/>, 30 minutes - see <c>ResidentPasswordRecoveryService.Lifetime</c>).
///
/// There is no navigation property to <c>ResidentAccessDTO</c> here on purpose, mirroring
/// <c>RefreshTokenDTO.SubjectId</c>'s precedent: this table is looked up by
/// <see cref="TokenHash"/> first, and the resident is loaded separately by
/// <see cref="ResidentId"/> only after the token itself is confirmed valid.
/// </summary>
public class ResidentPasswordRecoveryTokenDTO
{
    public Guid Id { get; set; }
    public Guid ResidentId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Set the moment this token is consumed by reset/change, OR when a later
    /// "forgot password" request supersedes it before it was ever used - either way, once
    /// this is non-null the token can never be used again. See
    /// <c>ResidentPasswordRecoveryService.IsValid</c>.</summary>
    public DateTime? UsedAt { get; set; }

    public string CreatedIp { get; set; } = string.Empty;
}

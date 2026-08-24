namespace CondotifyAPI.Domain.DTO.Users;

/// <summary>
/// A refresh token session, shared between staff (<c>UserAccess</c>) and resident
/// (<c>ResidentAccessDTO</c>) principals - <see cref="SubjectType"/> (see
/// <see cref="CondotifyAPI.Jwt.PrincipalTypes"/>) plus <see cref="SubjectId"/> is who it
/// belongs to. The token value itself is never stored: <see cref="TokenHash"/> is the
/// SHA-256 hash of the opaque value handed to the caller exactly once at issue/rotation
/// time, following the same pattern as <c>RegistrationInviteDTO.TokenHash</c>.
/// </summary>
public class RefreshTokenDTO
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    /// <summary>Contexto de condomínio preservado durante a rotação da sessão do morador.</summary>
    public Guid? ContextLicenseId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one via rotation, or null if this
    /// row was never rotated (still active, or revoked outright via logout/reuse).</summary>
    public string? ReplacedByHash { get; set; }

    public string DeviceLabel { get; set; } = string.Empty;
    public string CreatedIp { get; set; } = string.Empty;
}

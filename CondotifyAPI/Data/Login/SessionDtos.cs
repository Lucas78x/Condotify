namespace CondotifyAPI.Data.Login;

/// <summary>Body for POST /api/auth/refresh - anonymous, since the access token it
/// replaces may already be expired. The refresh token itself is the only credential.</summary>
public sealed class RefreshIn
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class RefreshOut
{
    /// <summary>"Success" or "InvalidToken" - mirrors <see cref="LoginOut.Result"/> and
    /// <c>ResidentLoginOut.Result</c>'s convention.</summary>
    public string Result { get; set; } = string.Empty;

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }

    /// <summary>Access token lifetime in seconds, read from
    /// <see cref="CondotifyAPI.Jwt.IJwtTokenService.AccessTokenLifetimeSeconds"/>.</summary>
    public long? ExpiresIn { get; set; }
}

/// <summary>Body for POST /api/auth/logout. The refresh token is optional (logout is
/// idempotent even with no body/an already-invalid value); when present, only a token
/// that belongs to the authenticated caller is ever revoked.</summary>
public sealed class LogoutIn
{
    public string? RefreshToken { get; set; }
}

/// <summary>One row of GET /api/auth/sessions - a single "device" the caller is (or was)
/// signed in on. Deliberately excludes the token hash and the identity of the subject
/// (both are implicit: this list is always scoped to the caller's own token).</summary>
public sealed class SessionOut
{
    public Guid Id { get; set; }
    public string DeviceLabel { get; set; } = string.Empty;
    public string CreatedIp { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

namespace CondotifyAPI.Data.Login;

public sealed class ResidentLoginIn
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional friendly device name (e.g. "iPhone de Lucas"), stored alongside
    /// the refresh token so a future "my sessions" screen (task 7) can show it. Never
    /// used for any authorization decision.</summary>
    public string? DeviceLabel { get; set; }
}

public sealed class ResidentLoginOut
{
    /// <summary>"Success" or "InvalidCredentials" - mirrors <c>LoginOut.Result</c>'s
    /// convention from the staff login endpoint.</summary>
    public string Result { get; set; } = string.Empty;

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }

    /// <summary>Access token lifetime in seconds, read from
    /// <see cref="CondotifyAPI.Jwt.IJwtTokenService.AccessTokenLifetimeSeconds"/> rather
    /// than a literal - a previous task fixed exactly the bug of hardcoding this value.</summary>
    public long? ExpiresIn { get; set; }

    public Guid? ResidentId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public ResidentAccessTypeEnum? AccessType { get; set; }

    public Guid? LicenseId { get; set; }
    public string? LicenseName { get; set; }
    public Guid? UnitId { get; set; }
    public string? UnitNumber { get; set; }
    public string? BlockName { get; set; }
}

/// <summary>Body for POST /api/auth/resident/password/forgot. Anonymous by nature - this is
/// how a resident who is locked out asks for help.</summary>
public sealed class ForgotPasswordIn
{
    public string? Email { get; set; }
}

/// <summary>The one response body /forgot ever returns - see
/// <c>ResidentAuthController.ForgotPasswordAcceptedBody</c>. Carrying no information about
/// whether the e-mail matched a resident is the entire point of this endpoint.</summary>
public sealed class ForgotPasswordOut
{
    public string Result { get; set; } = string.Empty;
}

/// <summary>Body for POST /api/auth/resident/password/reset - the recovery code e-mailed by
/// /forgot, plus the new password.</summary>
public sealed class ResidentResetPasswordIn
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Body for POST /api/auth/resident/password/change - requires the resident's
/// current password, unlike /reset which proves identity via the recovery token instead.
/// Named distinctly from <c>LoginIn.ChangePasswordIn</c> (the staff equivalent) - same
/// shape, different principal type, kept separate rather than shared across the
/// staff/resident boundary.</summary>
public sealed class ResidentChangePasswordIn
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Response for both /reset and /change on success ("Success") or failure (e.g.
/// "InvalidToken", "WrongCurrentPassword") - mirrors the Result-string convention already used
/// by ResidentLoginOut/RefreshOut.</summary>
public sealed class ResidentPasswordOperationOut
{
    public string Result { get; set; } = string.Empty;
    public string? Error { get; set; }
}

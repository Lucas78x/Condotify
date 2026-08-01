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

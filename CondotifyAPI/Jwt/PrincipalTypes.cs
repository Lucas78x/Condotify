namespace CondotifyAPI.Jwt
{
    /// <summary>
    /// Values for the "principal_type" claim, the sole signal that distinguishes a
    /// staff access token (issued from <see cref="Domain.Models.Users.UserAccess"/>)
    /// from a resident access token (issued from
    /// <see cref="Domain.DTO.Resident.ResidentAccessDTO"/>). Both principals are
    /// signed by the same issuer with the same key, so this claim - and the default
    /// authorization policy that requires it - is what keeps a resident token from
    /// satisfying a bare [Authorize] staff route.
    /// </summary>
    public static class PrincipalTypes
    {
        public const string Claim = "principal_type";
        public const string User = "user";
        public const string Resident = "resident";
    }
}

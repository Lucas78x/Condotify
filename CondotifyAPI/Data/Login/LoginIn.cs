namespace CondotifyAPI.Data.Login
{
    public sealed class LoginIn
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";

        /// <summary>Optional friendly device name (e.g. "Notebook do Lucas"), stored
        /// alongside the refresh token this login issues so GET /api/auth/sessions (task 7)
        /// can show it. Falls back to the User-Agent header, then "Desconhecido" - see
        /// CondotifyAPI.Controllers.SessionController.ResolveDeviceLabel. Never used for any
        /// authorization decision.</summary>
        public string? DeviceLabel { get; set; }
    }

    public sealed class VerifyMfaIn
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;

        /// <summary>Same purpose as <see cref="LoginIn.DeviceLabel"/> - the session (and its
        /// refresh token) is only actually created here when MFA is enabled, so this is
        /// where that label is captured for an MFA-protected account.</summary>
        public string? DeviceLabel { get; set; }
    }

    public sealed class MfaCodeIn
    {
        public string Code { get; set; } = string.Empty;
    }

    public sealed class ChangePasswordIn
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}

namespace CondotifyAPI.Data.Login
{
    public sealed class LoginIn
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class VerifyMfaIn
    {
        public string ChallengeToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
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

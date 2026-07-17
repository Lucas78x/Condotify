namespace CondotifyAPI.Data.Login
{
    public sealed class LoginOut
    {
        public string Result { get; set; } = "";
        public string? AccessToken { get; set; }
        public long? ExpiresIn { get; set; }
        public bool MfaRequired { get; set; }
        public string? ChallengeToken { get; set; }
    }

    public sealed class MfaSetupOut
    {
        public bool Enabled { get; set; }
        public string Secret { get; set; } = string.Empty;
        public string ProvisioningUri { get; set; } = string.Empty;
        public List<string> RecoveryCodes { get; set; } = [];
    }
}

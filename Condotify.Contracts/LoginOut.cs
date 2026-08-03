namespace Condotify.Out
{
    public sealed class LoginOut
    {
        public string Result { get; set; } = "";
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public long? ExpiresIn { get; set; }
        public bool MfaRequired { get; set; }
        public string? ChallengeToken { get; set; }
    }
}

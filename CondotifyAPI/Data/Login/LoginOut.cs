namespace CondotifyAPI.Data.Login
{
    public sealed class LoginOut
    {
        public string Result { get; set; } = "";
        public string? AccessToken { get; set; }
        public long? ExpiresIn { get; set; }
    }
}

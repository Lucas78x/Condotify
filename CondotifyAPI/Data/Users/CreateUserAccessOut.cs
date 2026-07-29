namespace CondotifyAPI.Data.Users;

public class CreateUserAccessOut
{
    public UserAccessCreateResult Result { get; set; }
    public string Errors { get; set; } = string.Empty;
}

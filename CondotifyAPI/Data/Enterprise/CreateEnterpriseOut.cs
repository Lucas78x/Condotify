namespace CondotifyAPI.Data.Enterprise;

public class CreateEnterpriseOut
{
    public EnterpriseCreateResult Result { get; set; }
    public string Errors { get; set; } = string.Empty;
}

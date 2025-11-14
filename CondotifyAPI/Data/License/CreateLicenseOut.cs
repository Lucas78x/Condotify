using CondotifyAPI.Domain.Models.License;

namespace CondotifyAPI.Data.Enterprise;

public class CreateLicenseOut
{
    public LicenseCreateResult Result { get; set; }
    public License License { get; set; }
    public string Errors { get; set; }
}

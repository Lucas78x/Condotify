namespace Condotify.Models;

public sealed class MfaSecurityViewModel
{
    public bool Enabled { get; set; }
    public string Secret { get; set; } = string.Empty;
    public string ProvisioningUri { get; set; } = string.Empty;
    public List<string> RecoveryCodes { get; set; } = [];
}

public sealed class ChangePasswordViewModel
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

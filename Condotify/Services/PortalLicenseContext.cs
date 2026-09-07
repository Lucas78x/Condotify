using Condotify.Models;

namespace Condotify.Services;

// Scoped to the authenticated Blazor circuit. Server authorization remains authoritative.
public sealed class PortalLicenseContext
{
    public LicenseViewModel? License { get; private set; }
    public LicenseAdministrationViewModel? Administration { get; private set; }
    public event Action? Changed;

    public void Select(LicenseViewModel? license, LicenseAdministrationViewModel? administration = null)
    {
        License = license;
        Administration = administration;
        Changed?.Invoke();
    }

    public bool Has(LicensePermission permission) => Administration?.CurrentAccess.Has(permission) == true;
    public string Workspace(string section) => License is null ? "/licencas" : LicenseRoutes.Workspace(License, section);
    public string Concierge => License is null ? "/portaria" : $"/portaria?licenseId={License.Id}";
}

using Condotify.Models;
using Condotify.Services;

namespace Condotify.Mobile.Services;

public sealed class MobileAppState(CondotifyApiClient api)
{
    private const string LicenseKey = "condotify.selected-license";
    public IReadOnlyList<LicenseViewModel> Licenses { get; private set; } = [];
    public Guid? SelectedLicenseId { get; private set; }
    public LicenseViewModel? SelectedLicense => Licenses.FirstOrDefault(x => Guid.TryParse(x.Id, out var id) && id == SelectedLicenseId);
    public event Action? Changed;

    public long ResidentEnabledModules { get; private set; } = (long)Condotify.Models.LicenseModuleEnum.All;

    public void SetResidentModules(long enabledModules)
    {
        if (ResidentEnabledModules == enabledModules) return;
        ResidentEnabledModules = enabledModules;
        Changed?.Invoke();
    }

    public async Task<ApiResult<IReadOnlyList<LicenseViewModel>>> LoadLicensesAsync(CancellationToken cancellationToken = default)
    {
        var result = await api.GetLicensesAsync(cancellationToken);
        if (!result.Success)
            return ApiResult<IReadOnlyList<LicenseViewModel>>.Fail(result.Error ?? "Nao foi possivel carregar os condominios.", result.StatusCode);

        Licenses = result.Value ?? [];
        var saved = Preferences.Default.Get(LicenseKey, string.Empty);
        var selected = Guid.TryParse(saved, out var savedId) && Licenses.Any(x => Guid.TryParse(x.Id, out var id) && id == savedId)
            ? savedId
            : Licenses.Select(x => Guid.TryParse(x.Id, out var id) ? id : Guid.Empty).FirstOrDefault(x => x != Guid.Empty);
        SelectLicense(selected == Guid.Empty ? null : selected);
        return ApiResult<IReadOnlyList<LicenseViewModel>>.Ok(Licenses);
    }

    public void SelectLicense(Guid? licenseId)
    {
        if (licenseId.HasValue && !Licenses.Any(x => Guid.TryParse(x.Id, out var id) && id == licenseId.Value))
            licenseId = null;
        SelectedLicenseId = licenseId;
        if (licenseId.HasValue) Preferences.Default.Set(LicenseKey, licenseId.Value.ToString("D"));
        else Preferences.Default.Remove(LicenseKey);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Licenses = [];
        SelectedLicenseId = null;
        ResidentEnabledModules = (long)Condotify.Models.LicenseModuleEnum.All;
        Preferences.Default.Remove(LicenseKey);
        Changed?.Invoke();
    }
}

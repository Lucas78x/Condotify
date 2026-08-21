using Condotify.Models;
using Condotify.Services;

namespace Condotify.Mobile.Services;

public sealed class MobileAppState
{
    private const string LicenseKey = "condotify.selected-license";
    private const string GroupSingularKey = "condotify.structure.group-singular";
    private const string GroupPluralKey = "condotify.structure.group-plural";
    private const string UnitSingularKey = "condotify.structure.unit-singular";
    private const string UnitPluralKey = "condotify.structure.unit-plural";
    private readonly CondotifyApiClient _api;

    public MobileAppState(CondotifyApiClient api)
    {
        _api = api;
        GroupLabelSingular = ReadLabel(GroupSingularKey, "Bloco");
        GroupLabelPlural = ReadLabel(GroupPluralKey, "Blocos");
        UnitLabelSingular = ReadLabel(UnitSingularKey, "Unidade");
        UnitLabelPlural = ReadLabel(UnitPluralKey, "Unidades");
    }

    public IReadOnlyList<LicenseViewModel> Licenses { get; private set; } = [];
    public Guid? SelectedLicenseId { get; private set; }
    public LicenseViewModel? SelectedLicense => Licenses.FirstOrDefault(x => Guid.TryParse(x.Id, out var id) && id == SelectedLicenseId);
    public event Action? Changed;

    public long ResidentEnabledModules { get; private set; } = (long)Condotify.Models.LicenseModuleEnum.All;
    public string GroupLabelSingular { get; private set; }
    public string GroupLabelPlural { get; private set; }
    public string UnitLabelSingular { get; private set; }
    public string UnitLabelPlural { get; private set; }

    public void SetResidentContext(ResidentProfileViewModel profile)
    {
        ResidentEnabledModules = profile.EnabledModules;
        SetNomenclature(
            profile.GroupLabelSingular,
            profile.GroupLabelPlural,
            profile.UnitLabelSingular,
            profile.UnitLabelPlural,
            notify: false);
        Changed?.Invoke();
    }

    public async Task<ApiResult<IReadOnlyList<LicenseViewModel>>> LoadLicensesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _api.GetLicensesAsync(cancellationToken);
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
        var selected = SelectedLicense;
        if (selected is not null)
            SetNomenclature(selected.GroupLabelSingular, selected.GroupLabelPlural, selected.UnitLabelSingular, selected.UnitLabelPlural, notify: false);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Licenses = [];
        SelectedLicenseId = null;
        ResidentEnabledModules = (long)Condotify.Models.LicenseModuleEnum.All;
        Preferences.Default.Remove(LicenseKey);
        ResetNomenclature();
        Changed?.Invoke();
    }

    private void SetNomenclature(string? groupSingular, string? groupPlural, string? unitSingular, string? unitPlural, bool notify)
    {
        GroupLabelSingular = NormalizeLabel(groupSingular, "Bloco");
        GroupLabelPlural = NormalizeLabel(groupPlural, "Blocos");
        UnitLabelSingular = NormalizeLabel(unitSingular, "Unidade");
        UnitLabelPlural = NormalizeLabel(unitPlural, "Unidades");
        Preferences.Default.Set(GroupSingularKey, GroupLabelSingular);
        Preferences.Default.Set(GroupPluralKey, GroupLabelPlural);
        Preferences.Default.Set(UnitSingularKey, UnitLabelSingular);
        Preferences.Default.Set(UnitPluralKey, UnitLabelPlural);
        if (notify) Changed?.Invoke();
    }

    private void ResetNomenclature()
    {
        GroupLabelSingular = "Bloco";
        GroupLabelPlural = "Blocos";
        UnitLabelSingular = "Unidade";
        UnitLabelPlural = "Unidades";
        Preferences.Default.Remove(GroupSingularKey);
        Preferences.Default.Remove(GroupPluralKey);
        Preferences.Default.Remove(UnitSingularKey);
        Preferences.Default.Remove(UnitPluralKey);
    }

    private static string ReadLabel(string key, string fallback) =>
        NormalizeLabel(Preferences.Default.Get(key, fallback), fallback);

    private static string NormalizeLabel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

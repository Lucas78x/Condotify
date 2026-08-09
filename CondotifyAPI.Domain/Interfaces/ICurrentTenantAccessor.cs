namespace CondotifyAPI.Domain.Interfaces;

public interface ICurrentTenantAccessor
{
    HashSet<Guid>? AccessibleLicenseIds { get; }
    Guid? AccessibleEnterpriseId { get; }
    void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId);
}

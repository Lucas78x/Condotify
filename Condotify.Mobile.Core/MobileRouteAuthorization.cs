using Condotify.Models;

namespace Condotify.Mobile.Core;

public static class MobileRouteAuthorization
{
    private static readonly HashSet<string> SharedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "home", "profile", "visitors", "bookings", "cameras", "deliveries",
        "ocorrencias", "notifications", "more"
    };

    private static readonly HashSet<string> StaffRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "concierge", "access", "alerts", "audit", "devices", "people", "units", "licenses"
    };

    private static readonly HashSet<string> ResidentRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "financeiro", "boletos", "documentos", "comunicados", "assembleias"
    };

    public static bool IsAllowed(MobilePrincipalKind principal, string? path)
        => IsAllowed(principal, path, (long)LicenseModuleEnum.All);

    public static bool IsAllowed(MobilePrincipalKind principal, string? path, long enabledModules)
    {
        if (principal is MobilePrincipalKind.None) return false;
        var firstSegment = (path ?? string.Empty)
            .Trim()
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        if (RequiredModule(firstSegment) is { } module && (enabledModules & (long)module) == 0)
            return false;

        return SharedRoutes.Contains(firstSegment) ||
               principal == MobilePrincipalKind.Staff && StaffRoutes.Contains(firstSegment) ||
               principal == MobilePrincipalKind.Resident && ResidentRoutes.Contains(firstSegment);
    }

    private static LicenseModuleEnum? RequiredModule(string route) => route.ToLowerInvariant() switch
    {
        "cameras" => LicenseModuleEnum.Cameras,
        "devices" => LicenseModuleEnum.Devices,
        "deliveries" => LicenseModuleEnum.Deliveries,
        "bookings" => LicenseModuleEnum.Bookings,
        "ocorrencias" => LicenseModuleEnum.Incidents,
        "financeiro" => LicenseModuleEnum.Finance,
        "boletos" => LicenseModuleEnum.Finance,
        "documentos" => LicenseModuleEnum.Documents,
        "comunicados" => LicenseModuleEnum.Announcements,
        "assembleias" => LicenseModuleEnum.Assemblies,
        _ => null
    };
}

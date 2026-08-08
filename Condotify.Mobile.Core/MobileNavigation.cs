using Condotify.Models;

namespace Condotify.Mobile.Core;

public sealed record MobileNavigationItem(string Route, string Label, string IconKey, LicenseModuleEnum? Module = null);

public static class MobileNavigation
{
    private static readonly MobileNavigationItem[] StaffItems =
    [
        new("/home", "Início", "home"),
        new("/concierge", "Portaria", "concierge"),
        new("/cameras", "Câmeras", "camera", LicenseModuleEnum.Cameras),
        new("/more", "Mais", "more")
    ];

    private static readonly MobileNavigationItem[] ResidentItems =
    [
        new("/home", "Início", "home"),
        new("/visitors", "Visitantes", "visitors"),
        new("/bookings", "Reservas", "calendar", LicenseModuleEnum.Bookings),
        new("/more", "Mais", "more")
    ];

    public static IReadOnlyList<MobileNavigationItem> For(MobilePrincipalKind principal) =>
        For(principal, (long)LicenseModuleEnum.All);

    public static IReadOnlyList<MobileNavigationItem> For(MobilePrincipalKind principal, long enabledModules)
    {
        var items = principal == MobilePrincipalKind.Resident ? ResidentItems : StaffItems;
        return items.Where(x => x.Module is null || (enabledModules & (long)x.Module.Value) != 0).ToList();
    }

    public static bool TryResolveDeepLink(string? value, out string route) =>
        MobileDeepLinks.TryNormalize(value, out route);
}

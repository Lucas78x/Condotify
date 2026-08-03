using Condotify.Models;

namespace Condotify.Mobile.Core;

public sealed record MobileNavigationItem(string Route, string Label, string IconKey);

public static class MobileNavigation
{
    private static readonly MobileNavigationItem[] StaffItems =
    [
        new("/home", "Inicio", "home"),
        new("/concierge", "Portaria", "concierge"),
        new("/cameras", "Cameras", "camera"),
        new("/more", "Mais", "more")
    ];

    private static readonly MobileNavigationItem[] ResidentItems =
    [
        new("/home", "Inicio", "home"),
        new("/visitors", "Visitantes", "visitors"),
        new("/bookings", "Reservas", "calendar"),
        new("/more", "Mais", "more")
    ];

    public static IReadOnlyList<MobileNavigationItem> For(MobilePrincipalKind principal) =>
        principal == MobilePrincipalKind.Resident ? ResidentItems : StaffItems;

    public static bool TryResolveDeepLink(string? value, out string route) =>
        MobileDeepLinks.TryNormalize(value, out route);
}

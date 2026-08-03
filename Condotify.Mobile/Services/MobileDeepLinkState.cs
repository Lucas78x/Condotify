using Condotify.Mobile.Core;

namespace Condotify.Mobile.Services;

public sealed class MobileDeepLinkState
{
    private string? _pendingRoute;
    public event Action<string>? Received;

    public bool Publish(string? value)
    {
        if (!MobileNavigation.TryResolveDeepLink(value, out var route)) return false;
        _pendingRoute = route;
        Received?.Invoke(route);
        return true;
    }

    public string? Consume()
    {
        var route = _pendingRoute;
        _pendingRoute = null;
        return route;
    }
}

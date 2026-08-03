namespace Condotify.Mobile.Services;

public sealed class MobileConnectivityState : IDisposable
{
    public MobileConnectivityState()
    {
        IsOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline { get; private set; }
    public event Action? Changed;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        var isOnline = args.NetworkAccess == NetworkAccess.Internet;
        if (isOnline == IsOnline) return;
        IsOnline = isOnline;
        Changed?.Invoke();
    }

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
}

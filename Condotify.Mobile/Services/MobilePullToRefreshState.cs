namespace Condotify.Mobile.Services;

// One pull-to-refresh gesture is wired at the shell level (MainLayout), not
// per page -- the app scrolls the whole document, not a nested container.
// The page currently on screen registers its own reload here; MainLayout
// just invokes whatever is registered when the gesture fires.
public sealed class MobilePullToRefreshState
{
    private Func<Task>? _handler;

    public void Register(Func<Task> handler) => _handler = handler;

    public void Unregister(Func<Task> handler)
    {
        // Register(LoadAsync) and Unregister(LoadAsync) each box a fresh
        // Func<Task> from the same method group -- Delegate.Equals (value
        // equality on target+method) is what matches them, not reference
        // equality, which would never clear a stale handler on dispose.
        if (Equals(_handler, handler)) _handler = null;
    }

    public Task RefreshAsync() => _handler?.Invoke() ?? Task.CompletedTask;
}

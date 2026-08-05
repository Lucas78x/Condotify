using System.Collections.Concurrent;

namespace CondotifyAPI.Services.Lpr;

public interface ILprDebounceStore
{
    bool WasRecentlyTriggered(Guid deviceId, string plate, TimeSpan window);
    void MarkTriggered(Guid deviceId, string plate);
}

// Per-instance, in-memory: correct for a single API instance. If the API
// ever scales horizontally, this needs to move to a shared store (e.g.
// Redis) so two instances don't both act on the same plate. Not needed
// today - documented here instead of built ahead of the requirement.
public sealed class InMemoryLprDebounceStore : ILprDebounceStore
{
    private readonly ConcurrentDictionary<(Guid DeviceId, string Plate), DateTime> _lastTriggeredAt = new();

    public bool WasRecentlyTriggered(Guid deviceId, string plate, TimeSpan window) =>
        _lastTriggeredAt.TryGetValue((deviceId, plate), out var lastTriggeredAt) &&
        DateTime.UtcNow - lastTriggeredAt < window;

    public void MarkTriggered(Guid deviceId, string plate) =>
        _lastTriggeredAt[(deviceId, plate)] = DateTime.UtcNow;
}

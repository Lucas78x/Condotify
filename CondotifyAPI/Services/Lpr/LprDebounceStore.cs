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
    // Longest debounce window LprDeviceProcessor allows is 300s
    // (Lpr:DebounceSeconds is clamped to [1, 300]). Retaining entries for
    // well beyond that keeps behavior identical from the caller's
    // perspective while still bounding memory for a long-running,
    // busy-gate process that would otherwise accumulate one entry per
    // distinct (device, plate) pair forever.
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(Guid DeviceId, string Plate), DateTime> _lastTriggeredAt = new();

    public bool WasRecentlyTriggered(Guid deviceId, string plate, TimeSpan window) =>
        _lastTriggeredAt.TryGetValue((deviceId, plate), out var lastTriggeredAt) &&
        DateTime.UtcNow - lastTriggeredAt < window;

    public void MarkTriggered(Guid deviceId, string plate)
    {
        _lastTriggeredAt[(deviceId, plate)] = DateTime.UtcNow;
        PruneStaleEntries();
    }

    // Opportunistic sweep-on-write: no timers, no new dependency, self
    // contained. Cheap relative to the snapshot+OCR round trip that always
    // precedes a MarkTriggered call.
    private void PruneStaleEntries()
    {
        var cutoff = DateTime.UtcNow - RetentionWindow;
        foreach (var (key, lastTriggeredAt) in _lastTriggeredAt)
        {
            if (lastTriggeredAt < cutoff)
                _lastTriggeredAt.TryRemove(key, out _);
        }
    }
}

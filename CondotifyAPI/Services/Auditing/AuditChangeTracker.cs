using System.Text.Json;

namespace CondotifyAPI.Services.Auditing;

public static class AuditChangeTracker
{
    public static IReadOnlyList<string> GetChangedFieldNames(object before, object after)
    {
        using var beforeDocument = JsonDocument.Parse(JsonSerializer.Serialize(before));
        using var afterDocument = JsonDocument.Parse(JsonSerializer.Serialize(after));
        var beforeValues = beforeDocument.RootElement.EnumerateObject()
            .ToDictionary(x => x.Name, x => x.Value.GetRawText(), StringComparer.OrdinalIgnoreCase);

        return afterDocument.RootElement.EnumerateObject()
            .Where(x => !beforeValues.TryGetValue(x.Name, out var previous) || previous != x.Value.GetRawText())
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ToList();
    }
}

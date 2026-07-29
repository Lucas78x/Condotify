namespace CondotifyAPI.Data.RecycleBin;

public sealed class RecycleBinItemOut
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string DeletedBy { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int DaysRemaining { get; set; }
}

public sealed class RecycleBinRestoreOut
{
    public Guid ItemId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

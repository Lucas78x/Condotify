namespace CondotifyAPI.Data.Operations;

public sealed class OperationalAlertPageOut
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Acknowledged { get; set; }
    public int Critical { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<OperationalAlertOut> Items { get; set; } = [];
}

public sealed class OperationalAlertSummaryOut
{
    public int Active { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Suppressed { get; set; }
}

public sealed class OperationalAlertActionIn
{
    public string Note { get; set; } = string.Empty;
}

public sealed class SnoozeOperationalAlertIn
{
    public int Minutes { get; set; } = 60;
    public string Note { get; set; } = string.Empty;
}

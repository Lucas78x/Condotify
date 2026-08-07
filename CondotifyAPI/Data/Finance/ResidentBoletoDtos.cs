namespace CondotifyAPI.Data.Finance;

public sealed class ResidentBoletoOut
{
    public Guid DocumentId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

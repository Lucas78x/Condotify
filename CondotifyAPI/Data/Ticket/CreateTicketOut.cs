namespace CondotifyAPI.Data.Tickets;

public class CreateTicketOut
{
    public TicketCreateResult Result { get; set; }
    public Guid? TicketId { get; set; }
    public string? Errors { get; set; }
}
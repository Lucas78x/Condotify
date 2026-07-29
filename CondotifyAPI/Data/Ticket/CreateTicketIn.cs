
using CondotifyAPI.Commands.Tickets;

namespace DigitalWorldOnline.Management.Api.Data;

public class CreateTicketIn
{
    public Guid UnitId { get; set; }
        public string Title { get; set; } = string.Empty;
    public DateTime ExpiredDate { get; set; }

    /// <summary>
    /// Reference Owner
    /// </summary>
    public Guid LicenseId { get; set; }

    /// <summary>
    /// Segunda via
    /// </summary>
    public bool IsSecondCopy { get; set; }
    public Guid? OriginalTicketId { get; set; }
}
public class CreateTicketResultModel
{
    public TicketCreateResult Result { get; set; }
    public Guid? TicketId { get; set; }

    public static CreateTicketResultModel Success(Guid ticketId)
        => new()
        {
            Result = TicketCreateResult.Created,
            TicketId = ticketId
        };

    public static CreateTicketResultModel Fail(TicketCreateResult result)
        => new()
        {
            Result = result
        };
}

public static class CreateTicketInConverter
{
    public static CreateTicketCommand ToCommand(this CreateTicketIn ticket)
    {
        return new CreateTicketCommand(
            ticket.UnitId,
            ticket.Title,
            ticket.ExpiredDate,
            ticket.LicenseId,
            ticket.IsSecondCopy,
            ticket.OriginalTicketId
        );
    }
}

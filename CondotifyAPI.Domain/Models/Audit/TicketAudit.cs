namespace CondotifyAPI.Domain.Models.Audit
{
    public class TicketAudit
    {
        public Guid Id { get; private set; }
        public Guid TicketId { get; private set; }
        public string Action { get; private set; } = string.Empty;
        public TicketStatusTypeEnum Status { get; private set; }
        public DateTime Date { get; private set; }

        protected TicketAudit() { }

        public TicketAudit(
            Guid ticketId,
            string action,
            TicketStatusTypeEnum status,
            DateTime date)
        {
            Id = Guid.NewGuid();
            TicketId = ticketId;
            Action = action;
            Status = status;
            Date = date;
        }
    }
}

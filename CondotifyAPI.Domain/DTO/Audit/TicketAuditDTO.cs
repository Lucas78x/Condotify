using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.DTO.Users;

namespace CondotifyAPI.Domain.DTO.Audit
{
    public class TicketAuditDTO
    {
        public Guid Id { get; set; }

        public Guid TicketId { get; set; }
        public TicketDTO Ticket { get; set; }

        public ActionTypeEnum Action { get; set; }

        public Guid UserId { get; set; }
        public UserAccessDTO User { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
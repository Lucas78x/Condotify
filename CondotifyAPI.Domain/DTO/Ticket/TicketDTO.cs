

using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Ticket
{
    public class TicketDTO
    {
        public Guid Id { get; set; }

        public Guid UnitId { get; set; }

        public string Title { get; set; }

        public string Barcode { get; set; }

        public string BarcodeUrl { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime ExpiredDate { get; set; }

        public TicketStatusTypeEnum Status { get; set; }

        public bool IsSecondCopy { get; set; }
        public Guid? OriginalTicketId { get; set; }

        public List<TicketAuditDTO> Audit { get; set; }

        /// <summary>
        /// Reference Owner
        /// </summary>
        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; }
    }

}

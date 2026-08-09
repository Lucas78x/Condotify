

using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Ticket
{
    public class TicketDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
    {
        public Guid Id { get; set; }

        public Guid UnitId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Barcode { get; set; } = string.Empty;

        public string BarcodeUrl { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; }

        public DateTime ExpiredDate { get; set; }

        public TicketStatusTypeEnum Status { get; set; }

        public bool IsSecondCopy { get; set; }
        public Guid? OriginalTicketId { get; set; }

        public List<TicketAuditDTO> Audit { get; set; } = [];

        /// <summary>
        /// Reference Owner
        /// </summary>
        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; } = null!;
    }

}

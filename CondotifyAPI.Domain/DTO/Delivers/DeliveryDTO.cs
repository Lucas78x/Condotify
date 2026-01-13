

using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Delivers
{
    public class DeliveryDTO
    {
        public Guid Id { get; set; }

        public DeliveryTypeEnum Type { get; set; }
        public DeliveryStatusEnum Status { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }

        public string TrackingCode { get; set; }

        public string PhotoUrl { get; set; }
        public string DeliveryProofUrl { get; set; }

        public Guid? ReceivedId { get; set; }
        public string ReceivedBy { get; set; }
        public DateTime? ReceivedAt { get; set; }

        public Guid? DeliveredToId { get; set; }
        public string DeliveredTo { get; set; }
        public DateTime? DeliveredAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; }

    }

}

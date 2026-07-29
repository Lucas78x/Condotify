

using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Delivers
{
    public class DeliveryDTO
    {
        public Guid Id { get; set; }

        public DeliveryTypeEnum Type { get; set; }
        public DeliveryStatusEnum Status { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string TrackingCode { get; set; } = string.Empty;

        public string PhotoUrl { get; set; } = string.Empty;
        public string DeliveryProofUrl { get; set; } = string.Empty;

        public Guid? ReceivedId { get; set; }
        public string ReceivedBy { get; set; } = string.Empty;
        public DateTime? ReceivedAt { get; set; }

        public Guid? DeliveredToId { get; set; }
        public string DeliveredTo { get; set; } = string.Empty;
        public DateTime? DeliveredAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; } = null!;

    }

}

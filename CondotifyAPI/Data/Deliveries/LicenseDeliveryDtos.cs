using CondotifyAPI.Domain.DTO.Delivers;

namespace CondotifyAPI.Data.Deliveries;

public class CreateDeliveryIn
{
    public DeliveryTypeEnum Type { get; set; } = DeliveryTypeEnum.Outros;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TrackingCode { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
}

public class UpdateDeliveryStatusIn
{
    public DeliveryStatusEnum Status { get; set; }
    public Guid? PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string ProofUrl { get; set; } = string.Empty;
}

public class DeliveryOut
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int StatusValue { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TrackingCode { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string DeliveryProofUrl { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
    public DateTime? ReceivedAt { get; set; }
    public string DeliveredTo { get; set; } = string.Empty;
    public DateTime? DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

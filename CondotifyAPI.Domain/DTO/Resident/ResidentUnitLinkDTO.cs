using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Domain.DTO.Resident;

public class ResidentUnitLinkDTO
{
    public Guid Id { get; set; }
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public ResidentUnitRelationshipEnum Relationship { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

using CondotifyAPI.Domain.DTO.Resident;

namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class ResidentAccessControlDTO
    {
        public Guid Id { get; set; }

        public long UserId { get; set; }
        public string CardNumber { get; set; } = string.Empty;
        public string TagNumber { get; set; } = string.Empty;
        public DeviceTypeEnum Type { get; set; }
        public bool IsActive { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Guid ResidentId { get; set; }
        public ResidentAccessDTO Resident { get; set; } = null!;
    }


}

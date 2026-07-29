using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Domain.DTO.Resident
{
    public class ResidentAccessCredentialDTO
    {
        public Guid Id { get; set; }

        public Guid ResidentId { get; set; }

        public ResidentAccessDTO Resident { get; set; } = null!;

        public AccessCredentialTypeEnum CredentialType { get; set; }

        /// <summary>
        /// Identificador lógico:
        /// - Face: "FACE-RESIDENT-123"
        /// - QrCode: payload ou ID
        /// - Card: número
        /// - Tag: código
        /// </summary>
        public string Identifier { get; set; } = string.Empty;

        public bool IsActive { get; set; }
        public bool IsTemporary { get; set; }
        public int RenewalCount { get; set; }
        public int MaxRenewals { get; set; }
        public int UseCount { get; set; }
        public int? MaxUses { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<ResidentAccessDeviceDTO> Devices { get; set; } = [];
    }
}

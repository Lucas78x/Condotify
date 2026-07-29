using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Domain.Models.Resident
{
    public class ResidentAccess
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string ApartmentNumber { get; set; } = string.Empty;
        public ResidentAccessTypeEnum AccessType { get; set; }
        public bool FirstAccess { get; set; }
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid UnitId { get; set; }
        public UnitDTO Unit { get; set; } = null!;
    }
}

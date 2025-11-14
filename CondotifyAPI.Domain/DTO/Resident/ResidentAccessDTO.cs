using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Domain.DTO.Resident
{
    public class ResidentAccessDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string CPF { get; set; }
        public string RG { get; set; }
        public string BirthDate { get; set; }
        public string ApartmentNumber { get; set; }
        public string ImgUrl { get; set; }
        public ResidentAccessTypeEnum AccessType { get; set; }
        public List<ResidentAccessControlDTO> Devices { get; set; }
        public bool FirstAccess { get; set; }
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid UnitId { get; set; }
        public UnitDTO Unit { get; set; }

    }
}

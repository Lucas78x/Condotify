using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.DTO.Invitation;

namespace CondotifyAPI.Domain.DTO.Resident
{
    public class ResidentAccessDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string CommercialPhone { get; set; } = string.Empty;
        public string CPF { get; set; }
        public string RG { get; set; }
        public string BirthDate { get; set; }
        public string ApartmentNumber { get; set; }
        public string ImgUrl { get; set; }
        public string Description { get; set; } = string.Empty;
        public ResidentAccessTypeEnum AccessType { get; set; }
        public ICollection<ResidentAccessCredentialDTO> AccessCredentials { get; set; }
        public bool FirstAccess { get; set; }
        public bool NotifyAccess { get; set; }
        public bool IsActive { get; set; } = true;
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid UnitId { get; set; }
        public UnitDTO Unit { get; set; }
        public ICollection<ResidentUnitLinkDTO> UnitLinks { get; set; } = new List<ResidentUnitLinkDTO>();
        public ICollection<VehicleDTO> Vehicles { get; set; } = new List<VehicleDTO>();
        public ICollection<RegistrationInviteDTO> RegistrationInvites { get; set; } = new List<RegistrationInviteDTO>();

    }
}

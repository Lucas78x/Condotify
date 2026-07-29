using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;

namespace CondotifyAPI.Domain.DTO.Enterprise
{
    public class EnterpriseDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;
        public string StateRegistration { get; set; } = string.Empty;
        public string MunicipalRegistration { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;

        public string Street { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Complement { get; set; } = string.Empty;
        public string Neighborhood { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public List<UserAccessDTO> Users { get; set; } = [];
        public List<LicenseDTO> Licenses { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }

        public string ContactPerson { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;

        public string LogoUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}

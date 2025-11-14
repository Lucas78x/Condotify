using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;

namespace CondotifyAPI.Domain.DTO.Enterprise
{
    public class EnterpriseDTO
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string CNPJ { get; set; } 
        public string StateRegistration { get; set; }
        public string MunicipalRegistration { get; set; } 

        public string Email { get; set; }
        public string Phone { get; set; }
        public string Mobile { get; set; }
        public string Website { get; set; }

        public string Street { get; set; }
        public string Number { get; set; }
        public string Complement { get; set; }
        public string Neighborhood { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
        public List<UserAccessDTO> Users { get; set; }
        public List<LicenseDTO> Licenses { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsActive { get; set; }

        public string ContactPerson { get; set; } 
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }

        public string LogoUrl { get; set; } // Para exibir logo
        public string Notes { get; set; } // Observações gerais
    }
}

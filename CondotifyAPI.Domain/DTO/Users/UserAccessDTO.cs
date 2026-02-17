using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.Enterprise;

namespace CondotifyAPI.Domain.DTO.Users
{
    public class UserAccessDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; private set; }
        public string PhoneNumber { get; set; }
        public string CPF { get; set; }
        public string RG { get; set; }
        public string BirthDate { get; set; }

        public AccessTypeEnum AccessType { get; set; }
        public bool FirstAccess { get; set; }
        public List<UserAccessAuditDTO> Audit { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid? EnterpriseId { get; set; }
        public EnterpriseDTO Enterprise { get; set; }
    }
}

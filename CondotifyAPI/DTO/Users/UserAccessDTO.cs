using CondotifyAPI.DTO.Audit;
using CondotifyAPI.DTO.Enterprise;

namespace CondotifyAPI.DTO.Users
{
    public class UserAccessDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string PhoneNumber { get; set; }
        public string CPF { get; set; }
        public string RG { get; set; }
        public string BirthDate { get; set; }

        public AccessTypeEnum AccessType { get; set; }
        public bool FirstAccess { get; set; }
        public List<UserAccessAuditDTO> Audit { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid EnterpriseId { get; set; }
        public EnterpriseDTO EnterPrise { get; set; }
    }
}

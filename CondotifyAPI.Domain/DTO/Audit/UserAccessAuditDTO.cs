using CondotifyAPI.Domain.DTO.Users;

namespace CondotifyAPI.Domain.DTO.Audit
{
    public class UserAccessAuditDTO
    {
        public Guid Id { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public string CPU { get; set;} = string.Empty;
        public string GPU { get; set; } = string.Empty;
        public string RAM { get; set;} = string.Empty;
        public ActionTypeEnum Action { get; set; }

        public Guid UserId { get; set; }
        public UserAccessDTO User { get; set; } = null!;
        
    }
}

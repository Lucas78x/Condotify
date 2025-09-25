using CondotifyAPI.DTO.Users;

namespace CondotifyAPI.DTO.Audit
{
    public class UserAccessAuditDTO
    {
        public Guid Id { get; set; }
        public string MacAddress { get; set; }
        public string IPAddress { get; set; }
        public string CPU { get; set;}
        public string GPU { get; set; }
        public string RAM { get; set;}
        public ActionTypeEnum Action { get; set; }

        public Guid UserId { get; set; }
        public UserAccessDTO User { get; set; }
        
    }
}

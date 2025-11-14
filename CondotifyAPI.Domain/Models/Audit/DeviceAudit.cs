namespace CondotifyAPI.Domain.Models.Audit
{
    public class DeviceAudit
    {
        public Guid Id { get; set; }
        public ActionTypeEnum Action { get; set; }
        public string ChangedFields { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; }


    }
}

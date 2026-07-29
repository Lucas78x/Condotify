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

        private DeviceAudit(
            ActionTypeEnum action,
            string changedFields,
            Guid userId,
            string userName)
        {
            Id = Guid.NewGuid();
            Action = action;
            ChangedFields = changedFields;
            UserId = userId;
            UserName = userName;
            Timestamp = DateTime.UtcNow;
        }

        public static DeviceAudit Create(string ChangedFields, Guid userId, string userName, ActionTypeEnum action = ActionTypeEnum.Create)
        {
            return new DeviceAudit(action, ChangedFields, userId, userName);
        }

        public void Update(ActionTypeEnum action, string changedFields)
        {

            Action = action;
            ChangedFields = changedFields;
            Timestamp = DateTime.UtcNow;
        }
    }
}

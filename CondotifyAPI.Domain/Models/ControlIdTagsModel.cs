namespace CondotifyAPI.Domain.Models
{
    public class ControlIdTagsModel
    {
        public ControlIdTagsModel() { }

        public long id { get; set; }
        public string value { get; set; }
        public long user_id { get; set; }

        private ControlIdTagsModel(long newId, string newValue, long newUser_id)
        {
           id = id;
           value = newValue;
           user_id = newUser_id;
        }

        public static ControlIdTagsModel Create(long newId,string newValue, long newUser_id)
        {
            return new ControlIdTagsModel(newId, newValue, newUser_id);
        }
    }
}

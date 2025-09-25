using CondotifyAPI.Models.Resident;

namespace CondotifyAPI.Models
{
    public class Block
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<UnitModel> Units { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }
}

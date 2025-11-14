using CondotifyAPI.Domain.Models.Units;

namespace CondotifyAPI.Domain.Models
{
    public class Block
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<Unit> Units { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
    }
}

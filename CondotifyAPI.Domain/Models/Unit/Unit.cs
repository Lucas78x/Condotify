using CondotifyAPI.Domain.Models.Resident;

namespace CondotifyAPI.Domain.Models.Units
{
    public class Unit
    {
        public Guid Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public List<ResidentAccess> Residents { get; set; } = [];

    }
}

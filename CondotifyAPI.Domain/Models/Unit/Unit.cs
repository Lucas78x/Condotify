using CondotifyAPI.Domain.Models.Resident;

namespace CondotifyAPI.Domain.Models.Units
{
    public class Unit
    {
        public Guid Id { get; set; }
        public string Number { get; set; } 
        public string Floor { get; set; }
        public List<ResidentAccess> Residents { get; set; }

    }
}

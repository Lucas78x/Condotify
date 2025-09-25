using CondotifyAPI.Models.Resident;

namespace CondotifyAPI.Models.Unit
{
    public class UnitModel
    {
        public Guid Id { get; set; }
        public string Number { get; set; } 
        public string Floor { get; set; }
        public List<ResidentAccess> Residents { get; set; }

    }
}

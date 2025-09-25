using CondotifyAPI.DTO.Block;
using CondotifyAPI.DTO.Resident;

namespace CondotifyAPI.DTO.Unit
{
    public class UnitDTO
    {
        public Guid Id { get; set; }
        public string Number { get; set; }
        public string Floor { get; set; }
        public List<ResidentAccessDTO> Residents { get; set; }

        public Guid BlockId { get; set; }
        public BlockDTO Block { get; set; }
    }
}

using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;

namespace CondotifyAPI.Domain.DTO.Unit
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

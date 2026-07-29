using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Vehicle;

namespace CondotifyAPI.Domain.DTO.Unit
{
    public class UnitDTO
    {
        public Guid Id { get; set; }
        public string Number { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public List<ResidentAccessDTO> Residents { get; set; } = [];
        public List<ResidentUnitLinkDTO> ResidentLinks { get; set; } = new();
        public List<VehicleDTO> Vehicles { get; set; } = new();

        public Guid BlockId { get; set; }
        public BlockDTO Block { get; set; } = null!;
    }
}

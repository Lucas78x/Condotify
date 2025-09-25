using CondotifyAPI.DTO.License;
using CondotifyAPI.DTO.Resident;
using CondotifyAPI.DTO.Unit;

namespace CondotifyAPI.DTO.Block
{
    public class BlockDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<UnitDTO> Units { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }

        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; }
    }
}

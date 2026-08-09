using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Domain.DTO.Block
{
    public class BlockDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<UnitDTO> Units { get; set; } = [];
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }

        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; } = null!;
    }
}

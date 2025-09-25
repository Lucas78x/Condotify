using CondotifyAPI.DTO.Block;
using CondotifyAPI.DTO.Enterprise;
using CondotifyAPI.DTO.Equipments;
using CondotifyAPI.DTO.Location;

namespace CondotifyAPI.DTO.License
{
    public class LicenseDTO
    {
        /// <summary>
        /// Unique identifier for the entity (primary key)
        /// </summary>
        public Guid Id { get; set; }
        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        /// <value>
        /// The product name. This value is required and must be between 1 and 200 characters.
        /// </value>
        public string Name { get; set; }
        public string CNPJ { get; set; }

        public  OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }
        public List<BlockDTO> Blocks { get; set; }
        public List<AccessControlDeviceDTO> Devices { get; set; }
        public LocationDTO Location { get; set; }
        /// <summary>
        /// Timestamp indicating when the license expire date
        /// </summary>
        public DateTime ExpireDate { get; set; }

        /// <summary>
        /// Timestamp indicating when the entity was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        public Guid EnterpriseId { get; set; }
        public EnterpriseDTO Enterprise { get; set; }
    }
}

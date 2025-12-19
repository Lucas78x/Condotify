using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Location;

namespace CondotifyAPI.Domain.DTO.License
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

        //Enums
        public  OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }

        // Entitys
        public List<BlockDTO> Blocks { get; set; }
        public List<AccessControlDeviceDTO> Devices { get; set; }
        public List<CFTVDeviceDTO> CFTVDevices { get; set; }
        public LocationDTO Location { get; set; }

        //Times
        /// <summary>
        /// Timestamp indicating when the license expire date
        /// </summary>
        public DateTime ExpireDate { get; set; }

        /// <summary>
        /// Timestamp indicating when the entity was created
        /// </summary>
        public DateTime CreatedAt { get; set; }

        //Reference Owner
        public Guid EnterpriseId { get; set; }
        public EnterpriseDTO Enterprise { get; set; }
    }
}

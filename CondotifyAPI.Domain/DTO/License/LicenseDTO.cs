using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Amenities;

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
        public string Name { get; set; } = string.Empty;
        public string CNPJ { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string GroupLabelSingular { get; set; } = "Bloco";
        public string GroupLabelPlural { get; set; } = "Blocos";
        public string UnitLabelSingular { get; set; } = "Unidade";
        public string UnitLabelPlural { get; set; } = "Unidades";

        //Enums
        public  OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }

        // Entitys
        public List<BlockDTO> Blocks { get; set; } = [];
        public List<AccessControlDeviceDTO> Devices { get; set; } = [];
        public List<CFTVDeviceDTO> CFTVDevices { get; set; } = [];
        public List<DeliveryDTO> Deliveries { get; set; } = [];
        public List<TicketDTO> Tickets { get; set; } = [];
        public List<RegistrationInviteDTO> RegistrationInvites { get; set; } = new();
        public List<LicenseUserAccessDTO> UserAccesses { get; set; } = new();
        public LicenseCredentialPolicyDTO? CredentialPolicy { get; set; }
        public List<AccessRouteDTO> AccessRoutes { get; set; } = new();
        public List<AmenityDTO> Amenities { get; set; } = new();
        public LocationDTO Location { get; set; } = new();

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
        public EnterpriseDTO Enterprise { get; set; } = null!;
    }
}

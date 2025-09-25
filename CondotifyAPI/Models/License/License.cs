using CondotifyAPI.DTO.Equipments;
using CondotifyAPI.Models.Equipments;

namespace CondotifyAPI.Models.License
{
    public class License
    {
        public License() { }
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
        public OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }
        public List<Block> Blocks { get; set; }
        public List<AccessControlDevice> Devices { get; set; }
        public Location Location { get; set; }
        /// <summary>
        /// Timestamp indicating when the license expire date
        /// </summary>
        public DateTime ExpireDate { get; set; }
        /// <summary>
        /// Timestamp indicating when the entity was created
        /// </summary>
        public DateTime CreatedAt { get; set; }
        public bool IsExpired() => DateTime.UtcNow > ExpireDate;


        private License(string name, LicenseTypeEnum type, Location location, DateTime expireDate, DateTime createdAt)
        {
            Id = Guid.NewGuid();
            Name = name;
            Type = type;
            Location = location;
            ExpireDate = expireDate;
            CreatedAt = createdAt;
        }

        public static License Create(string name, LicenseTypeEnum type, Location location, DateTime expireDate, DateTime createdAt)
        {
            return new License(name, type, location, expireDate, createdAt);
        }

        public bool AddBlock(Block block)
        {
            if (Blocks == null)
                Blocks = new List<Block>();

            if (block == null && Blocks.Count >= byte.MaxValue)
                return false;

            Blocks.Add(block);
            return true;
        }

        public bool AddDevice(AccessControlDevice device)
        {
            if (Devices == null)
                Devices = new List<AccessControlDevice>();

            if (device == null && Devices.Count >= byte.MaxValue)
                return false;

            Devices.Add(device);
            return true;
        }
    }
  
}

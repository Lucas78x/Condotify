using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Domain.Models.License
{
    public class License
    {
        public License() { }

        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CNPJ { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Code { get; set; }


        public OrganizationTypeEnum Organization { get; set; }
        public BuildingTypeEnum Building { get; set; }
        public LicenseTypeEnum Type { get; set; }
        public List<Block> Blocks { get; set; }
        public List<AccessControlDevice> Devices { get; set; }
        public Location Location { get; set; }

        public DateTime ExpireDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsExpired() => DateTime.UtcNow > ExpireDate;


        private License(string name, string cnpj, LicenseTypeEnum type, Location location, DateTime expireDate, DateTime createdAt)
        {
            Id = Guid.NewGuid();
            Name = name;
            CNPJ = cnpj;
            Type = type;
            Location = location;
            ExpireDate = expireDate;
            CreatedAt = createdAt;
        }

        public static License Create(string name, string cnpj, LicenseTypeEnum type, Location location, DateTime expireDate, DateTime createdAt)
        {
            return new License(name, cnpj, type, location, expireDate, createdAt);
        }

        public void MaskCNPJ()
        {
            if (string.IsNullOrWhiteSpace(CNPJ))
                return;

            CNPJ = new string(CNPJ.Where(char.IsDigit).ToArray());

            if (CNPJ.Length == 14)
                CNPJ = $"{CNPJ[..2]}.***.***/{CNPJ.Substring(8, 4)}-**";
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

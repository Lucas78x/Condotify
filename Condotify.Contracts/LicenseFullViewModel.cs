namespace Condotify.Models
{
    public class LicenseFullViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string UrlKey { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalResidents { get; set; }
        public int TotalBlocks { get; set; }
        public int TotalUnits { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpireDate { get; set; }
        public bool IsExpired { get; set; }
        public long EnabledModules { get; set; }
        public string GroupLabelSingular { get; set; } = "Bloco";
        public string GroupLabelPlural { get; set; } = "Blocos";
        public string UnitLabelSingular { get; set; } = "Unidade";
        public string UnitLabelPlural { get; set; } = "Unidades";
        public List<BlockFrontViewModel> Blocks { get; set; } = new();
    }

    public class BlockFrontViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalUnits { get; set; }
        public int TotalResidents { get; set; }
    }
}

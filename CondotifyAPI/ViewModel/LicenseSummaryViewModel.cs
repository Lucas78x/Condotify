using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.License;

namespace CondotifyAPI.ViewModels
{
    public class LicenseSummaryViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
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

        public List<BlockSummaryViewModel> Blocks { get; set; } = new();

        public static LicenseSummaryViewModel? FromDomain(License? license)
        {
            if (license == null) return null;

            var blocks = license.Blocks?
                .Select(BlockSummaryViewModel.FromDomain)
                .Where(block => block != null)
                .Select(block => block!)
                .ToList() ?? new List<BlockSummaryViewModel>();

            var totalUnits = blocks.Sum(b => b.TotalUnits);
            var totalResidents = blocks.Sum(b => b.TotalResidents);

            return new LicenseSummaryViewModel
            {
                Id = license.Id,
                Name = license.Name,
                Code = license.Code,
                City = license.City,
                State = license.Country,

                Type = license.Type.ToString(),
                Status = license.IsExpired() ? "Expirada" : "Ativa",

                TotalResidents = totalResidents,
                TotalBlocks = blocks.Count,
                TotalUnits = totalUnits,

                CreatedAt = license.CreatedAt,
                ExpireDate = license.ExpireDate,
                IsExpired = license.IsExpired(),

                Blocks = blocks
            };
        }
    }

    public class BlockSummaryViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalUnits { get; set; }
        public int TotalResidents { get; set; }

        public static BlockSummaryViewModel? FromDomain(Block? block)
        {
            if (block == null) return null;

            var totalResidents = block.Units?
                .Sum(u => u.Residents?.Count ?? 0) ?? 0;

            return new BlockSummaryViewModel
            {
                Id = block.Id,
                Name = block.Name,
                TotalUnits = block.Units?.Count ?? 0,
                TotalResidents = totalResidents
            };
        }
    }
}

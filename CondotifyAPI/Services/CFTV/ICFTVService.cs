using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Services.CFTV
{
    public interface ICFTVService
    {
        Task<TestCftvConnectionOut> TestAsync(CFTVDevice device, CancellationToken ct = default);
    }
}
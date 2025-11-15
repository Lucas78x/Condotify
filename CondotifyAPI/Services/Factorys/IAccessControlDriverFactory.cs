using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Drivers;

namespace CondotifyAPI.Services.Factorys
{
    public interface IAccessControlDriverFactory
    {
        /// <summary>
        /// Retorna o driver que suporta o DeviceType informado.
        /// Lança NotSupportedException se nenhum driver estiver registrado.
        /// </summary>
        IAccessControlDriver GetDriver(DeviceTypeEnum type);
    }

}

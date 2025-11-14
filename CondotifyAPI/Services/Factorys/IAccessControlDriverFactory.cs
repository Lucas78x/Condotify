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

        /// <summary>
        /// Tenta obter um driver para o tipo informado.
        /// Retorna true e o driver quando encontrado, ou false quando não houver correspondência.
        /// </summary>
        bool TryGetDriver(DeviceTypeEnum type, out IAccessControlDriver? driver);

        /// <summary>
        /// Retorna o driver para o device (baseado em device.Type).
        /// Lança NotSupportedException se nenhum driver disponível.
        /// </summary>
        IAccessControlDriver GetDriverForDevice(AccessControlDevice device);
    }

}

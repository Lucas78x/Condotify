using CondotifyAPI.Domain.Models.Equipments;
using MediatR;

namespace CondotifyAPI.Queries
{
    public class GetAccessDeviceByDeviceIdQuery : IRequest<AccessControlDevice?>
    {
        public Guid DeviceId { get; }

        public GetAccessDeviceByDeviceIdQuery(Guid deviceId)
        {
            DeviceId = deviceId;
        }
    }
}

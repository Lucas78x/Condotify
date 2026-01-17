using MediatR;
using AutoMapper;
using CondotifyAPI.Queries;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Handlers.Queries
{
    public class GetAccessDeviceByDeviceIdQueryHandler
        : IRequestHandler<GetAccessDeviceByDeviceIdQuery, AccessControlDevice?>
    {
        private readonly ICondotifyQueriesRepository _repository;
        private readonly IMapper _mapper;

        public GetAccessDeviceByDeviceIdQueryHandler(
            ICondotifyQueriesRepository repository,
            IMapper mapper)
        {
            _repository = repository;
            _mapper = mapper;
        }

        public async Task<AccessControlDevice?> Handle(
            GetAccessDeviceByDeviceIdQuery request,
            CancellationToken cancellationToken)
        {
           return await _repository.GetDeviceByDeviceIdAsync(request.DeviceId);
        }
    }
}

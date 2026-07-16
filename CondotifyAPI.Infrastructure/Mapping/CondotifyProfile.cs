using AutoMapper;
using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.Audit;
using CondotifyAPI.Domain.Models.Enterprises;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Domain.Models.Ticket;
using CondotifyAPI.Domain.Models.Units;
using CondotifyAPI.Domain.Models.Users;

namespace CondotifyAPI.Infrastructure.Mapping;

public class CondotifyProfile : Profile
{
    public CondotifyProfile()
    {
        CreateMap<UserAccessDTO, UserAccess>()
            .ReverseMap();

        CreateMap<EnterpriseDTO, Enterprise>()
            .ReverseMap();

        CreateMap<LicenseDTO, License>()
        .ReverseMap();

        CreateMap<BlockDTO, Block>()
            .ReverseMap();

        CreateMap<UnitDTO, Unit>()
            .ReverseMap();

        CreateMap<ResidentAccessDTO, ResidentAccess>()
            .ReverseMap();

        CreateMap<AccessControlDeviceDTO, AccessControlDevice>()
            .ReverseMap();

        CreateMap<LocationDTO, Location>()
            .ReverseMap();

        CreateMap<DeviceAuditDTO, DeviceAudit>()
            .ReverseMap();

        CreateMap<CFTVDeviceDTO, CFTVDevice>()
            .ReverseMap();

        CreateMap<CFTVChannelDTO, CFTVChannel>()
            .ReverseMap();

        CreateMap<TicketDTO, Ticket>()
            .ReverseMap();

        CreateMap<TicketAudit, TicketAudit>();

    }
}

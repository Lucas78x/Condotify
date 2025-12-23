namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class CFTVChannelDTO
    {
        public Guid Id { get; set; }

        public int ChannelNumber { get; set; }

        public string Name { get; set; }

        public bool IsEnabled { get; set; }

        public string RtspPath { get; set; }

        public Guid CFTVDeviceId { get; set; }
        public CFTVDeviceDTO Device { get; set; }
    }
}
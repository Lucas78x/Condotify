namespace CondotifyAPI.Domain.Models.Equipments
{
    public class CFTVChannel
    {
        public Guid Id { get; set; }

        public int ChannelNumber { get; set; }

        public string Name { get; set; }

        public bool IsEnabled { get; set; }

        public string RtspPath { get; set; }

    }
}
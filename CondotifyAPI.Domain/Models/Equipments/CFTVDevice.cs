namespace CondotifyAPI.Domain.Models.Equipments
{
    public class CFTVDevice
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
        public string Password { get; set; }
        public string IpAddress { get; set; }
        public string HTTPPort { get; set; }
        public string RTSPPort { get; set; }
        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }
    }
}

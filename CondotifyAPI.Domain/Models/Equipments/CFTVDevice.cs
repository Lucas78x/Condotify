namespace CondotifyAPI.Domain.Models.Equipments
{
    public class CFTVDevice
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";
        public IpTypeEnum IpType { get; set; }
        public ScreenProportionEnum Proportion { get; set; }
        public MarkEnum Mark { get; set; }

        public CFTVDeviceTypeEnum DeviceType { get; set; }

        public int MaxChannels { get; set; }
        public bool ResidentVisible { get; set; }

        public ICollection<CFTVChannel> Channels { get; set; } = [];


        private CFTVDevice(string name,
         string userName,
         string password,
         string ipAddress,
         string hTTPPort,
         string rTSPPort,
         IpTypeEnum ipType,
         ScreenProportionEnum proportion,
         MarkEnum mark,
         CFTVDeviceTypeEnum deviceType,
         int maxChannels,
         ICollection<CFTVChannel> channels,
         bool residentVisible = false)
        {
            Id = Guid.NewGuid();
            Name = name;
            UserName = userName;
            Password = password;
            IpAddress = ipAddress;
            HTTPPort = hTTPPort;
            RTSPPort = rTSPPort;
            IpType = ipType;
            Proportion = proportion;
            Mark = mark;
            DeviceType = deviceType;
            MaxChannels = maxChannels;
            Channels = channels;
            ResidentVisible = residentVisible;
        }

        public static CFTVDevice Create(
            string name,
            string userName,
            string password,
            string ipAddress,
            string hTTPPort,
            string rTSPPort,
            IpTypeEnum ipType,
            ScreenProportionEnum proportion,
            MarkEnum mark,
            CFTVDeviceTypeEnum deviceType,
            int maxChannels,
            ICollection<CFTVChannel> channels,
            bool residentVisible = false)
        {
            return new CFTVDevice(
                name,
                userName,
                password,
                ipAddress,
                hTTPPort,
                rTSPPort,
                ipType,
                proportion,
                mark,
                deviceType,
                maxChannels,
                channels,
                residentVisible);
        }

        public void AddChannels(ICollection<int> channels)
        {
            if (Channels == null)
                Channels = [];

            foreach (var channel in channels)
            {
                var newChannel = new CFTVChannel();
                newChannel.ChannelNumber = channel;

                Channels.Add(newChannel);
            }
        }

    }
    public class CftvDeviceResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";
        public CFTVDeviceTypeEnum DeviceType { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

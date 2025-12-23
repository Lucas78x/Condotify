namespace CondotifyAPI.Data.Equipments
{
    public class TestCftvConnectionOut
    {
        // 🔌 Testes de nível device
        public bool PingOk { get; set; }
        public bool TcpRtspOk { get; set; }

        public ICollection<ChannelTestResultOut> Channels { get; set; }
            = new List<ChannelTestResultOut>();
    }
    public class ChannelTestResultOut
    {
        public int ChannelNumber { get; set; }

        public bool RtspOk { get; set; }

        public string? RtspUrlWorking { get; set; }

        public string? Error { get; set; }

        public ICollection<string> Attempts { get; set; }
            = new List<string>();
    }
}

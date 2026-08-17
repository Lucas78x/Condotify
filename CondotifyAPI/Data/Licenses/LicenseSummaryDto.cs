namespace CondotifyAPI.Data.Licenses
{
    public class LicenseSummaryDto
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = null!;
        public string Codigo { get; set; } = null!;
        public string UrlKey { get; set; } = null!;
        public int Moradores { get; set; }
        public string Cidade { get; set; } = null!;
        public string Estado { get; set; } = null!;
        public long EnabledModules { get; set; }
    }

}

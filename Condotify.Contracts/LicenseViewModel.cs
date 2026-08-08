namespace Condotify.Models
{
    public class LicenseViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string Codigo { get; set; } = string.Empty;
        public int Moradores { get; set; }
        public string Cidade { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public int ProjetoId { get; set; }
        public long EnabledModules { get; set; }
    }
}

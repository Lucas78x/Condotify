namespace Condotify.Models
{
    public class LicenseViewModel
    {
        public int Id { get; set; }
        public string Nome { get; set; }
        public string Codigo { get; set; }
        public int Moradores { get; set; }
        public string Cidade { get; set; }
        public string Estado { get; set; }
        public int ProjetoId { get; set; }
    }
}

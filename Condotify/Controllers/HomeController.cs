using Condotify.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Condotify.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Inicio()
        {
            var licencas = new List<LicenseViewModel>
                {
                    new LicenseViewModel {Id = 1, Nome="Apart Hotel Porto Smeralda", Codigo="13464", Moradores=164, Cidade="Camaçari", Estado="BA", ProjetoId=2340 },
                    new LicenseViewModel {Id = 2,Nome="Condomínio Adamas Guarajuba", Codigo="14735", Moradores=103, Cidade="Camaçari", Estado="BA", ProjetoId=2340 },
                    new LicenseViewModel {Id = 3, Nome="Costa Smeralda", Codigo="19194", Moradores=846, Cidade="Camaçari", Estado="BA", ProjetoId=2340 }
                };

            return View(licencas);
        }

        public IActionResult Detalhes(Guid id)
        {
            var model = new LicencaDetalhesViewModel
            {
                Id = id,
                Nome = "Apart Hotel Porto Smeralda",
                Blocos = new List<BlocoViewModel>
            {
                new() { Nome = "Bloco A", Unidades = 15, Moradores = 38 },
                new() { Nome = "Bloco ADM", Unidades = 1, Moradores = 10 },
                new() { Nome = "Bloco B", Unidades = 18, Moradores = 49 },
                new() { Nome = "Bloco C", Unidades = 33, Moradores = 77 }
            }
            };

            return View("~/Views/Licencas/Detalhes.cshtml",model);
        }

        public class LicencaDetalhesViewModel
        {
            public Guid Id { get; set; }
            public string Nome { get; set; }
            public List<BlocoViewModel> Blocos { get; set; }
        }

        public class BlocoViewModel
        {
            public string Nome { get; set; }
            public int Unidades { get; set; }
            public int Moradores { get; set; }
        }

        public IActionResult Login()
        {
            return View();
        }

        public IActionResult Licencas()
        {
            var licencas = new List<LicenseViewModel>
                {
                    new LicenseViewModel {Id = 1, Nome="Apart Hotel Porto Smeralda", Codigo="13464", Moradores=164, Cidade="Camaçari", Estado="BA", ProjetoId=2340 },
                    new LicenseViewModel {Id = 2,Nome="Condomínio Adamas Guarajuba", Codigo="14735", Moradores=103, Cidade="Camaçari", Estado="BA", ProjetoId=2340 },
                    new LicenseViewModel {Id = 3, Nome="Costa Smeralda", Codigo="19194", Moradores=846, Cidade="Camaçari", Estado="BA", ProjetoId=2340 }
                };

            return View("~/Views/Licencas/Licencas.cshtml", licencas);
        }


        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

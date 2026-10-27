using Microsoft.AspNetCore.Mvc;
using monkeyrace.Models;
using System.Diagnostics;

namespace monkeyrace.Controllers
{
    // šis controller valdo visus langus
    public class HomeController : Controller
    {
        public IActionResult Index() // '/' puslapis yra pats pagrindinis puslapis index.html. View() jį atidaro
        {
            return View();
        }

        [HttpGet("/privatumas")]
        public IActionResult Privacy() => View();

        // telefone footer nerodomas – jo turinys čia (mobilioji juosta „Apie“)
        [HttpGet("/apie")]
        public IActionResult About() => View();

        [HttpGet("/pamokos")]
        public IActionResult Lessons() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() // jeigu nutinka netikėta klaida, ji nusiunčia vartotoją į /home/erorr langą, kuriame parodoma klaida
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

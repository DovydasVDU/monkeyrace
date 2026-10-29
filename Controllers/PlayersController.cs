using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

// Viešas žaidėjo profilis ir paieška. Privatus ir neegzistuojantis žaidėjas gauna tą patį 404.
public class PlayersController : Controller
{
    private readonly AppDbContext _db;
    private readonly RaceService _raceService;

    public PlayersController(AppDbContext db, RaceService raceService)
    {
        _db = db;
        _raceService = raceService;
    }

    [HttpGet("/u/{username}")]
    [EnableRateLimiting("stats")]
    public async Task<IActionResult> Profile(string username)
    {
        Response.Headers["X-Robots-Tag"] = "noindex"; // paieškos sistemoms šių puslapių nerodome
        var profile = await PublicProfiles.GetAsync(_db, _raceService, username, PlayerIdentity.GetUserId(HttpContext));
        if (profile == null)
            return NotFound();
        ViewData["Username"] = profile.Card.Username;
        return View("Profile", profile);
    }

    [HttpGet("/api/user/{username}")]
    [EnableRateLimiting("stats")]
    public async Task<IActionResult> ProfileJson(string username)
    {
        Response.Headers["X-Robots-Tag"] = "noindex";
        var profile = await PublicProfiles.GetAsync(_db, _raceService, username, PlayerIdentity.GetUserId(HttpContext));
        return profile == null ? NotFound(new { message = "Žaidėjas nerastas." }) : Ok(profile);
    }

    [HttpGet("/api/users/search")]
    [EnableRateLimiting("stats")]
    public async Task<IActionResult> Search(string q)
    {
        Response.Headers["X-Robots-Tag"] = "noindex";
        return Ok(await PublicProfiles.SearchAsync(_db, q, PlayerIdentity.GetUserId(HttpContext) != null));
    }
}

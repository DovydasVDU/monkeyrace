using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

// Mėnesio iššūkis (/menesio-issukis): visi rašo tuos pačius ilgus žodžius, laiką matuoja serveris
public class ChallengeController : Controller
{
    private const string StartedKey = "ChallengeStartedAt";
    private const string MonthKey = "ChallengeMonth";

    private readonly AppDbContext _db;
    private readonly RaceService _raceService;

    public ChallengeController(AppDbContext db, RaceService raceService)
    {
        _db = db;
        _raceService = raceService;
    }

    [HttpGet("/menesio-issukis")]
    public async Task<IActionResult> Index()
    {
        var month = MonthlyChallenge.MonthKey(DateTime.UtcNow);
        var userId = PlayerIdentity.GetUserId(HttpContext);
        var top = await _db.ChallengeEntries.Where(e => e.Month == month)
            .OrderBy(e => e.BestScore).ThenBy(e => e.UpdatedAt).Take(10)
            .Select(e => new ChallengeRow { Username = e.User.Username, Score = e.BestScore, Seconds = e.BestSeconds, Errors = e.BestErrors })
            .ToListAsync();
        var mine = userId == null ? null : await _db.ChallengeEntries
            .FirstOrDefaultAsync(e => e.UserId == userId.Value && e.Month == month);
        return View(new ChallengePage
        {
            Month = month,
            Words = MonthlyChallenge.WordsFor(month),
            Top = top,
            Mine = mine,
            LoggedIn = userId != null
        });
    }

    // Paspaudus „Pradėti“: serveris užfiksuoja pradžios laiką
    [HttpPost("/challenge/start")]
    [EnableRateLimiting("lessons")]
    public IActionResult Start()
    {
        if (PlayerIdentity.GetUserId(HttpContext) == null)
            return Unauthorized();
        var month = MonthlyChallenge.MonthKey(DateTime.UtcNow);
        HttpContext.Session.SetString(StartedKey, DateTime.UtcNow.ToString("O"));
        HttpContext.Session.SetString(MonthKey, month);
        return Json(new { words = MonthlyChallenge.WordsFor(month), countdown = MonthlyChallenge.CountdownSeconds });
    }

    public sealed class FinishDto
    {
        public int Errors { get; set; }
    }

    [HttpPost("/challenge/finish")]
    [EnableRateLimiting("lessons")]
    public async Task<IActionResult> Finish([FromBody] FinishDto dto)
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Unauthorized();
        var month = HttpContext.Session.GetString(MonthKey);
        if (dto == null || month == null || !DateTime.TryParse(HttpContext.Session.GetString(StartedKey), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt))
            return BadRequest("Challenge unavailable.");
        HttpContext.Session.Remove(StartedKey);
        HttpContext.Session.Remove(MonthKey);

        // laikas nuo „Pradėti“ be atgalinės atskaitos; jei mėnuo jau pasikeitė – bandymas negalioja
        var seconds = (DateTime.UtcNow - startedAt).TotalSeconds - MonthlyChallenge.CountdownSeconds;
        if (month != MonthlyChallenge.MonthKey(DateTime.UtcNow) || dto.Errors < 0 || dto.Errors > 500)
            return BadRequest("Challenge unavailable.");
        if (!MonthlyChallenge.IsPlausible(month, seconds))
            return BadRequest("Implausible result.");

        return Json(await _raceService.SaveChallengeAsync(userId.Value, month, seconds, dto.Errors));
    }
}

public sealed class ChallengeRow
{
    public string Username { get; init; } = "";
    public double Score { get; init; }
    public double Seconds { get; init; }
    public int Errors { get; init; }
}

public sealed class ChallengePage
{
    public string Month { get; init; } = "";
    public System.Collections.Generic.IReadOnlyList<string> Words { get; init; } = Array.Empty<string>();
    public System.Collections.Generic.List<ChallengeRow> Top { get; init; } = new();
    public ChallengeEntry Mine { get; init; }
    public bool LoggedIn { get; init; }
}

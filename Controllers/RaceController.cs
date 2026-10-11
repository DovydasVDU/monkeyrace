using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
//saugumas ir [Authorize] 
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
// Controller, atsakingas už žaidimo logiką, tekstų parinkimą ir rezultatų saugojimą
public class RaceController : Controller
{
    private readonly AppDbContext _db;
    private readonly RaceService _raceService;
    private readonly MatchmakingService _matchmaking;

    public RaceController(
        AppDbContext db,
        RaceService raceService,
        MatchmakingService matchmaking)
    {
        _db = db;
        _raceService = raceService; // prijungiame duomenų bazę per Dependency Injection, kad galėtume saugoti rezultatus ir gauti tekstus iš duomenų bazės
        _matchmaking = matchmaking;
    }

    public async Task<IActionResult> Play() // pagrindinis žaidimo puslapis, kuris parenka tekstą pagal sunkumą ir rodo jį žaidėjui
    {
        // iš sesijos gauname paskutinių parinktų tekstų Id, kad galėtume jų vengti, kai parenkame naują tekstą žaidimui (testuojant man tai nepatiko)
        var lastIdsJson = HttpContext.Session.GetString("LastSnippetIds"); 

        var lastIds = string.IsNullOrEmpty(lastIdsJson)
            ? new List<int>() // tikriname, ar sesijoje yra saugomi ID's. Jei nėra sukuriame JSON listą, jei yra, tai jį deserializuojame į List<int>
            : JsonSerializer.Deserialize<List<int>>(lastIdsJson)!;

        // paimame atsitiktinį tekstą iš duomenų bazės pagal sunkumą ir lastSnippets
        var snippet = await _raceService.GetRandomSnippetAsync(lastIds); // čia naudojame iš raceService. _ - tai yra konvencija, kad tai yra private laukas, kuris yra naudojamas tik šioje klasėje

        // lastIds logika paprasta - saugome paskutinius tris, jei jų padaugėja - pirmąjį pašaliname
        lastIds.Add(snippet.Id);
        if (lastIds.Count > 3)
            lastIds.RemoveAt(0);

        HttpContext.Session.SetString("LastSnippetIds", JsonSerializer.Serialize(lastIds)); // atnaujiname ir išsaugom sesijoje lastIds

        // Race įrašas DB kuriamas tik išsaugant rezultatą (SaveResult), o ne kiekvieną kartą atidarius puslapį.
        // Sesijoje prisimenam, kokį tekstą ir kada davėme - pagal tai SaveResult patikrina rezultatą.
        HttpContext.Session.SetInt32("SoloSnippetId", snippet.Id);
        HttpContext.Session.SetString("SoloStartedAt", DateTime.UtcNow.ToString("O"));

        return View(snippet);
    }

    [HttpGet("/race/quick")]
    public async Task<IActionResult> Quick(string username, int attempt = 0)
    {
        if (attempt >= 2)
            return View("MatchmakingError");

        var playerKey = PlayerIdentity.GetPlayerKey(HttpContext);
        var displayName = PlayerIdentity.GetUsername(HttpContext, username);
        HttpContext.Session.SetString("MatchmakingUsername", displayName);
        var userId = PlayerIdentity.GetUserId(HttpContext);
        // kortelė keliauja su žaidėju, kad trasoje užvedus pele nereikėtų papildomų užklausų
        var card = userId == null ? null : await _raceService.GetPlayerCardAsync(userId.Value);
        var raceId = _matchmaking.JoinOrCreate(playerKey, displayName, userId, card);

        return RedirectToAction(nameof(MultiPlay), new { raceId, attempt });
    }

    [HttpGet("/race/multiplay/{raceId}")]
    public IActionResult MultiPlay(string raceId)
    {
        var playerKey = PlayerIdentity.GetPlayerKey(HttpContext);
        var username = PlayerIdentity.GetUsername(HttpContext);
        var attempt = 0;
        if (Request.Query.TryGetValue("attempt", out var attemptValue))
            int.TryParse(attemptValue, out attempt);

        ViewBag.RaceId = raceId;
        ViewBag.PlayerKey = playerKey;
        ViewBag.Username = username;
        ViewBag.Attempt = attempt;
        return View("MultiPlay");
    }

    [HttpPost]
    // Šis metodas priima solo rezultatus iš frontend'o. Prisijungusiam įrašo rezultatą ir skiria XP/bananus,
    // svečiui grąžina tik hipotetinį apdovanojimą.
    public async Task<IActionResult> SaveResult([FromBody] RaceResultDto dto) 
    {
        if (dto == null) 
        {
            return BadRequest("Invalid payload.");
        }

        // Naršyklei aklai netikime: tekstas ir pradžios laikas imami iš sesijos (nustatyti Play metu),
        // o WPM negali viršyti greičio, kurį leidžia serverio išmatuotas laikas nuo puslapio atidarymo.
        var snippetId = HttpContext.Session.GetInt32("SoloSnippetId");
        var startedAtText = HttpContext.Session.GetString("SoloStartedAt");
        if (snippetId == null || !DateTime.TryParse(startedAtText, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt))
            return BadRequest("Race unavailable.");
        // vienas rezultatas vienam Play atidarymui
        HttpContext.Session.Remove("SoloSnippetId");
        HttpContext.Session.Remove("SoloStartedAt");

        var snippet = await _db.TextSnippets.FindAsync(snippetId.Value);
        if (snippet == null)
            return BadRequest("Race unavailable.");

        var elapsedMinutes = Math.Max(1d / 60, (DateTime.UtcNow - startedAt).TotalMinutes);
        var maxPossibleWpm = snippet.Content.Length / 5d / elapsedMinutes * 1.2;
        if (dto.Wpm < 0 || dto.Wpm > RaceService.MaxWpm || dto.Wpm > maxPossibleWpm ||
            dto.Accuracy < 0 || dto.Accuracy > 100)
            return BadRequest("Implausible result.");

        // Svečias: tik parodome, kiek būtų gavęs. Nieko nesaugome.
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Json(new { success = true, reward = RaceService.GuestReward(RaceMode.Solo, null, dto.Wpm, dto.Accuracy) });

        var race = new Race { TextSnippetId = snippet.Id, StartedAt = startedAt };
        _db.Races.Add(race);
        await _db.SaveChangesAsync();

        var reward = await _raceService.SaveRaceResultAsync(userId.Value, race.Id, RaceMode.Solo, null,
            dto.Wpm, dto.Accuracy, dto.TimeSeconds, dto.ErrorCount);

        return Json(new { success = true, reward }); 
    }
}
// DTO (Data Transfer Object) klasė, skirta priimti rezultatus iš frontend'o ir saugoti juos duomenų bazėje
public class RaceResultDto 
{
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int TimeSeconds { get; set; }
    public int ErrorCount { get; set; }
}
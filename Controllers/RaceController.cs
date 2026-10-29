using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        await StartSoloAsync(snippet.Id, null);
        return View(snippet);
    }

    // Žodžių režimas: atsitiktiniai dažniausi žodžiai, rašoma tol, kol baigiasi pasirinktas laikas
    [HttpGet("/race/words")]
    public async Task<IActionResult> Words(int seconds = 30)
    {
        if (!WordService.AllowedSeconds.Contains(seconds))
            return BadRequest("Unsupported duration.");

        await StartSoloAsync(null, seconds);
        ViewBag.Duration = seconds;
        return View("Play", new TextSnippet { Content = WordService.Generate(seconds) });
    }

    // Treniruoklis iš žaidėjo klaidų. XP neskiriama (SaveResult nekviečiamas), kad nebūtų lengvo būdo jį rinkti;
    // klaidos vis tiek siunčiamos, todėl teisingai parašyti žodžiai pamažu tampa išmoktais.
    [HttpGet("/race/train")]
    public async Task<IActionResult> Train()
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Redirect("/");

        var text = await _raceService.BuildTrainingTextAsync(userId.Value);
        if (text == null)
        {
            ViewBag.Heading = "Dar nėra ką treniruoti";
            ViewBag.Message = "Sužaisk kelias lenktynes – treniruoklis surinks žodžius ir raides, kuriose klysti.";
            ViewBag.RetryUrl = "/race/quick";
            ViewBag.RetryLabel = "Žaisti lenktynes";
            return View("MatchmakingError");
        }

        ViewBag.Training = true;
        return View("Play", new TextSnippet { Content = text });
    }

    // Sesijoje prisimenam, kokį tekstą ir kada davėme - pagal tai SaveResult patikrina rezultatą.
    // Prisijungusiam Race įrašas kuriamas iškart: du lygiagretūs SaveResult gauna tą patį Id,
    // todėl SaveRaceResultAsync apdovanoja tik kartą (sesija neužrakinama).
    private async Task StartSoloAsync(int? snippetId, int? duration)
    {
        var session = HttpContext.Session;
        session.Remove("SoloSnippetId");
        session.Remove("SoloDuration");
        session.Remove("SoloRaceId");
        if (snippetId != null)
            session.SetInt32("SoloSnippetId", snippetId.Value);
        if (duration != null)
            session.SetInt32("SoloDuration", duration.Value);
        var startedAt = DateTime.UtcNow;
        session.SetString("SoloStartedAt", startedAt.ToString("O"));

        if (PlayerIdentity.GetUserId(HttpContext) == null)
            return;
        var race = new Race { TextSnippetId = snippetId, DurationSeconds = duration, StartedAt = startedAt };
        _db.Races.Add(race);
        await _db.SaveChangesAsync();
        session.SetInt32("SoloRaceId", race.Id);
    }

    [HttpGet("/race/quick")]
    public async Task<IActionResult> Quick(string username, int attempt = 0, int seconds = 0)
    {
        if (seconds != 0 && !WordService.AllowedSeconds.Contains(seconds))
            return BadRequest("Unsupported duration.");
        if (attempt >= 2)
            return View("MatchmakingError");

        var playerKey = PlayerIdentity.GetPlayerKey(HttpContext);
        var displayName = PlayerIdentity.GetUsername(HttpContext, username);
        HttpContext.Session.SetString("MatchmakingUsername", displayName);
        var userId = PlayerIdentity.GetUserId(HttpContext);
        // kortelė keliauja su žaidėju, kad trasoje užvedus pele nereikėtų papildomų užklausų
        var card = userId == null ? null : await _raceService.GetPlayerCardAsync(userId.Value);
        var raceId = _matchmaking.JoinOrCreate(playerKey, displayName, userId, card, seconds);

        // seconds lieka adrese, kad pakartotinė paieška liktų tame pačiame režime
        return RedirectToAction(nameof(MultiPlay), new { raceId, attempt, seconds = seconds == 0 ? (int?)null : seconds });
    }

    // Lenktynės tik su botais; levels – kiekvieno boto charakteris, pvz. "adaptive,easy,hard"
    [HttpGet("/race/bots")]
    public async Task<IActionResult> Bots(string levels, string username, int attempt = 0)
    {
        var parsed = (levels ?? "adaptive,adaptive,adaptive").Split(',')
            .Select(value => Enum.TryParse<BotLevel>(value, true, out var level) &&
                Enum.IsDefined(level) && !int.TryParse(value, out _) ? level : (BotLevel?)null)
            .ToArray();
        if (parsed.Length > 3 || parsed.Any(level => level == null))
            return BadRequest("Unsupported bot level.");
        if (attempt >= 2)
            return View("MatchmakingError");

        var playerKey = PlayerIdentity.GetPlayerKey(HttpContext);
        var displayName = PlayerIdentity.GetUsername(HttpContext, username);
        HttpContext.Session.SetString("MatchmakingUsername", displayName);
        var userId = PlayerIdentity.GetUserId(HttpContext);
        var card = userId == null ? null : await _raceService.GetPlayerCardAsync(userId.Value);
        var raceId = _matchmaking.CreateBotRace(playerKey, displayName, userId, card,
            parsed.Select(level => level.Value).ToArray());

        // bots lieka adrese, kad „Žaisti dar kartą“ grąžintų į tas pačias lenktynes su botais
        return RedirectToAction(nameof(MultiPlay), new { raceId, attempt,
            bots = string.Join(",", parsed).ToLowerInvariant() });
    }

    [HttpGet("/race/room/create")]
    public async Task<IActionResult> CreateRoom(string username)
    {
        if (!SetFriendName(username))
            return Redirect("/#friends");
        var (raceId, _) = _matchmaking.CreateFriendRoom(PlayerIdentity.GetPlayerKey(HttpContext),
            PlayerIdentity.GetUsername(HttpContext), PlayerIdentity.GetUserId(HttpContext), await FriendCardAsync());
        return RedirectToFriendRoom(raceId);
    }

    // Ta pati nuoroda tinka pakvietimui ir revanšui („Žaisti dar kartą“ po lenktynių)
    [HttpGet("/race/room/join")]
    public async Task<IActionResult> JoinRoom(string code, string username, int attempt = 0)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!MatchmakingService.IsValidCode(code))
            return RoomError("Neteisingas kambario kodas. Jį sudaro 5 raidės ir skaičiai.", code);
        if (attempt >= 2)
            return RoomError("Nepavyko prisijungti prie kambario. Pabandyk dar kartą.", code);
        // svečias be vardo (pvz. atidarė pakvietimo nuorodą) – pirma įveda vardą pradžios puslapyje
        if (!SetFriendName(username))
            return Redirect($"/?room={code}#friends");

        var (raceId, error) = _matchmaking.JoinFriendRoom(code, PlayerIdentity.GetPlayerKey(HttpContext),
            PlayerIdentity.GetUsername(HttpContext), PlayerIdentity.GetUserId(HttpContext), await FriendCardAsync());
        return error switch
        {
            "NotFound" => RoomError($"Kambario {code} nėra. Gal jis jau uždarytas?", code),
            "Started" => RoomError("Šiame kambaryje lenktynės jau vyksta. Palauk, kol baigsis, ir bandyk vėl.", code),
            "Full" => RoomError("Kambarys pilnas – jame jau 4 žaidėjai.", code),
            _ => RedirectToFriendRoom(raceId, attempt)
        };
    }

    // Prisijungusiam vardas imamas iš paskyros; svečias privalo jį įvesti, kad draugai jį atpažintų
    private bool SetFriendName(string username)
    {
        if (User.Identity?.IsAuthenticated == true)
            return true;
        if (!string.IsNullOrWhiteSpace(username))
            HttpContext.Session.SetString("MatchmakingUsername", PlayerIdentity.GuestName(username));
        return HttpContext.Session.GetString("MatchmakingUsername") != null;
    }

    private async Task<PlayerCard> FriendCardAsync()
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        return userId == null ? null : await _raceService.GetPlayerCardAsync(userId.Value);
    }

    // room null, jei žaidėjas grąžintas į kitas vykstančias lenktynes (ne draugų kambarį)
    private IActionResult RedirectToFriendRoom(string raceId, int attempt = 0) =>
        RedirectToAction(nameof(MultiPlay), new { raceId, attempt, room = _matchmaking.CodeOf(raceId) });

    private IActionResult RoomError(string message, string code)
    {
        ViewBag.Message = message;
        ViewBag.RetryUrl = MatchmakingService.IsValidCode(code) ? $"/?room={code}#friends" : "/#friends";
        return View("MatchmakingError");
    }

    // Top 10 rezultato lenktynių perklausa; laiko juosta įrašoma tik naujoms klasikinėms lenktynėms
    [HttpGet("/race/replay/{resultId:int}")]
    public async Task<IActionResult> Replay(int resultId)
    {
        var result = await _db.RaceResults
            .Where(r => r.Id == resultId && r.Race.ReplayJson != null)
            .Select(r => new { r.UserId, r.User.Username, r.WPM, r.CompletedAt, r.Race.ReplayJson, Text = r.Race.TextSnippet.Content })
            .FirstOrDefaultAsync();
        if (result == null)
        {
            ViewBag.Heading = "Perklausos nėra";
            ViewBag.Message = "Šių lenktynių perklausos nėra – ji įrašoma tik naujoms online lenktynėms.";
            ViewBag.RetryUrl = "/#top10-title";
            ViewBag.RetryLabel = "Į Top 10";
            Response.StatusCode = 404;
            return View("MatchmakingError");
        }

        ViewBag.ResultId = resultId;
        ViewBag.UserId = result.UserId;
        ViewBag.Username = result.Username;
        ViewBag.Wpm = result.WPM;
        ViewBag.Date = result.CompletedAt.ToString("yyyy-MM-dd");
        // JSON eina į <script>: "<" galimas tik eilutėse, ten < reiškia tą patį, o </script> nebeužsidaro
        ViewBag.ReplayJson = result.ReplayJson.Replace("<", "\\u003c");
        // klasikinėse lenktynėse room.Text = snippet.Content; Serialize pakeičia < į <
        ViewBag.TextJson = JsonSerializer.Serialize(result.Text);
        return View();
    }

    [HttpGet("/race/multiplay/{raceId}")]
    public IActionResult MultiPlay(string raceId)
    {
        var playerKey = PlayerIdentity.GetPlayerKey(HttpContext);
        var attempt = 0;
        if (Request.Query.TryGetValue("attempt", out var attemptValue))
            int.TryParse(attemptValue, out attempt);

        ViewBag.RaceId = raceId;
        ViewBag.PlayerKey = playerKey;
        ViewBag.Attempt = attempt;
        int.TryParse(Request.Query["seconds"], out var seconds);
        ViewBag.Seconds = seconds;
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
        var duration = HttpContext.Session.GetInt32("SoloDuration");
        var raceId = HttpContext.Session.GetInt32("SoloRaceId");
        var startedAtText = HttpContext.Session.GetString("SoloStartedAt");
        if ((snippetId == null && duration == null) || !DateTime.TryParse(startedAtText, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt))
            return BadRequest("Race unavailable.");
        // vienas rezultatas vienam Play / Words atidarymui
        HttpContext.Session.Remove("SoloSnippetId");
        HttpContext.Session.Remove("SoloDuration");
        HttpContext.Session.Remove("SoloRaceId");
        HttpContext.Session.Remove("SoloStartedAt");

        var elapsed = DateTime.UtcNow - startedAt;
        double maxPossibleWpm = RaceService.MaxWpm;
        if (duration != null)
        {
            // laikmatis naršyklėje startuoja nuo pirmo klavišo, todėl nuo puslapio atidarymo praeina bent visa trukmė
            if (elapsed.TotalSeconds < duration.Value - 1)
                return BadRequest("Implausible result.");
        }
        else
        {
            var snippet = await _db.TextSnippets.FindAsync(snippetId.Value);
            if (snippet == null)
                return BadRequest("Race unavailable.");
            var elapsedMinutes = Math.Max(1d / 60, elapsed.TotalMinutes);
            maxPossibleWpm = snippet.Content.Length / 5d / elapsedMinutes * 1.2;
        }
        if (dto.Wpm < 0 || dto.Wpm > RaceService.MaxWpm || dto.Wpm > maxPossibleWpm ||
            dto.Accuracy < 0 || dto.Accuracy > 100)
            return BadRequest("Implausible result.");

        // Svečias: tik parodome, kiek būtų gavęs. Nieko nesaugome.
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Json(new { success = true, reward = RaceService.GuestReward(RaceMode.Solo, null, dto.Wpm, dto.Accuracy, duration) });
        // puslapis atidarytas dar neprisijungus
        if (raceId == null)
            return BadRequest("Race unavailable.");

        var reward = await _raceService.SaveRaceResultAsync(userId.Value, raceId.Value, RaceMode.Solo, null,
            dto.Wpm, dto.Accuracy, dto.TimeSeconds, dto.ErrorCount);

        return Json(new { success = true, reward });
    }

    // Raidžių ir žodžių klaidos po bet kurių lenktynių (solo, žodžių, online). Svečiams nesaugoma.
    [HttpPost("/race/mistakes")]
    public async Task<IActionResult> SaveMistakes([FromBody] MistakesDto dto)
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Unauthorized();
        if (dto?.Keys == null || dto.Words == null ||
            dto.Keys.Count > MistakesDto.MaxKeys || dto.Words.Count > MistakesDto.MaxWords ||
            dto.Keys.Any(key => key == null || key.Count > MistakesDto.MaxKeyCount) ||
            dto.Words.Any(word => word == null))
            return BadRequest("Invalid payload.");

        await _raceService.SaveMistakesAsync(userId.Value, dto);
        return Ok();
    }
}

public class MistakesDto
{
    public const int MaxKeys = 300;
    // 120 s žodžių režimas su RaceService.MaxWpm – iki 500 žodžių
    public const int MaxWords = 600;
    public const int MaxKeyCount = 5000;
    public const int MaxWordLength = 40;

    public List<KeyCount> Keys { get; set; }
    public List<WordTry> Words { get; set; }

    public class KeyCount
    {
        public string Expected { get; set; }
        public string Typed { get; set; }
        public int Count { get; set; }
    }

    public class WordTry
    {
        public string Word { get; set; }
        public bool HadError { get; set; }
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
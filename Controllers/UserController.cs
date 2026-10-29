using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

// Atsakingas už vartotojo profilio puslapį, kuriame rodomi vidutiniai rezultatai ir paskutinių žaidimų statistika (po 10, kitus 10 užkrauna PastRaces)
public class UserController : Controller
{
    // kiek paskutinių lenktynių rodoma iš karto ir kiek pridedama paspaudus „Užkrauti daugiau“
    private const int PastRacesPageSize = 10;

    private readonly AppDbContext _db;
    private readonly RaceService _raceService;

    public UserController(AppDbContext db, RaceService raceService) // per Dependency Injection gaunamas prisijungiame prie duomenų bazės
    {
        _db = db;
        _raceService = raceService;
    }

    [HttpGet]
    [Authorize]
    // Šis metodas rodo vartotojo profilio puslapį su vidutiniais rezultatais ir paskutinių 10 žaidimų statistika.
    // Pirmiausia tikrinama, ar vartotojas prisijungęs, tada gaunami visi jo žaidimai iš duomenų bazės, apskaičiuojami vidurkiai,
    // ir galiausiai sukuriamas UserProfileViewModel objektas, kuris perduodamas į View.
    public async Task<IActionResult> Stats()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier); // tikriname, ar prisijungęs
        if (string.IsNullOrEmpty(userIdStr)) // dar kartą patikriname, ar tikrai yra prisijungęs, jei ne - grąžiname į pagrindinį puslapį
            return Redirect("/");

        var userId = int.Parse(userIdStr);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId); // surandame vartotoją duomenų bazėje pagal ID
        if (user == null)
            return Redirect("/");

        var results = await _db.RaceResults // ištraukiame iš duomenų bazės informaciją apie user pagal ID
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CompletedAt)
            .ToListAsync();

        var model = new UserProfileViewModel // sukuriame duomenų objektą, kuris saugo rezultatus
        {
            Username = user.Username,
            AverageWpm = results.Any() ? (int)results.Average(r => r.WPM) : 0, // su klaustukai tikriname, ar yra informacija, jei ne - grąžiname "unknown" arba "0"
            AverageAccuracy = results.Any() ? results.Average(r => r.Accuracy) : 0,
            TotalRaces = results.Count,
            // atskira projekcija, kad nebūtų kraunamas ReplayJson
            PastRaces = await QueryPastRaces(userId, 0),
            Mistakes = await _raceService.GetMistakeStatsAsync(userId),
            OwnedItems = (await _db.UserItems.Where(i => i.UserId == userId).Select(i => i.ItemId).ToListAsync()).ToHashSet(),
            EquippedItems = new[]
            {
                ShopCatalog.Equipped(user.HatId, ShopCatalog.Hats).Id,
                ShopCatalog.Equipped(user.FurId, ShopCatalog.Furs).Id,
                ShopCatalog.Equipped(user.TrackId, ShopCatalog.Tracks).Id,
                ShopCatalog.Equipped(user.BackgroundId, ShopCatalog.Backgrounds).Id,
                ShopCatalog.Equipped(user.FrameId, ShopCatalog.Frames).Id
            }.ToHashSet()
        };

        return View("Stats", model); // perudodame į /Stats puslapį
    }

    // Profilio „Užkrauti daugiau“: kitos PastRacesPageSize lenktynių eilutės (HTML partial) nuo skip-osios
    [HttpGet("/User/PastRaces")]
    [Authorize]
    public async Task<IActionResult> PastRaces(int skip)
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Unauthorized();
        return PartialView("_PastRaceRows", await QueryPastRaces(userId.Value, Math.Max(0, skip)));
    }

    // Id kaip antras rikiavimo raktas, kad puslapiai nesikartotų, jei dvi lenktynės baigėsi tuo pačiu momentu
    private Task<List<PastRaceViewModel>> QueryPastRaces(int userId, int skip) =>
        _db.RaceResults
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(PastRacesPageSize)
            .Select(r => new PastRaceViewModel
            {
                ResultId = r.Id,
                CompletedAt = r.CompletedAt,
                Wpm = r.WPM,
                Accuracy = r.Accuracy,
                ErrorCount = r.ErrorCount,
                HasReplay = r.Race.ReplayJson != null
            })
            .ToListAsync();

    // Parduotuvė: nupirkti (jei dar neturi) ir užsidėti. Forma iš Stats.cshtml, atsakymas – žinutė parduotuvėje.
    [HttpPost("/shop/equip/{itemId}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Equip(string itemId)
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        if (userId == null)
            return Redirect("/");
        var error = await _raceService.EquipItemAsync(userId.Value, itemId);
        var item = ShopCatalog.Find(itemId);
        TempData["ShopMessage"] = error ?? $"Užsidėta: {item.Name}.";
        TempData["ShopError"] = error != null;
        return Redirect($"/User/Stats?tab={item?.Category}#shop");
    }

    // Paskyros ištrynimas (privatumo puslapis /privatumas): reikia dar kartą įvesti slaptažodį
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(string password)
    {
        var userId = PlayerIdentity.GetUserId(HttpContext);
        var user = userId == null ? null : await _db.Users.FindAsync(userId.Value);
        if (user == null)
            return Redirect("/");
        if (string.IsNullOrEmpty(password) || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            TempData["DeleteError"] = "Neteisingas slaptažodis.";
            return Redirect("/User/Stats#account");
        }

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            // kitų žaidėjų perklausose lieka tik „Ištrintas žaidėjas“ be userId
            var marker = $"\"userId\":{user.Id},";
            var races = await _db.Races.Where(r => r.ReplayJson != null && r.ReplayJson.Contains(marker)).ToListAsync();
            foreach (var race in races)
                race.ReplayJson = RaceAnalyzer.AnonymizeReplay(race.ReplayJson, user.Id);
            await _db.SaveChangesAsync();

            await _db.RaceResults.Where(r => r.UserId == user.Id).ExecuteDeleteAsync();
            await _db.UserKeyStats.Where(k => k.UserId == user.Id).ExecuteDeleteAsync();
            await _db.UserWordMistakes.Where(w => w.UserId == user.Id).ExecuteDeleteAsync();
            await _db.UserItems.Where(i => i.UserId == user.Id).ExecuteDeleteAsync();
            await _db.UserLessonProgresses.Where(p => p.UserId == user.Id).ExecuteDeleteAsync();
            await _db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return Redirect("/");
    }
}

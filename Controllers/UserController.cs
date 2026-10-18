using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

// Atsakingas už vartotojo profilio puslapį, kuriame rodomi vidutiniai rezultatai ir paskutinių 20 žaidimų statistika
public class UserController : Controller
{
    private readonly AppDbContext _db;
    private readonly RaceService _raceService;

    public UserController(AppDbContext db, RaceService raceService) // per Dependency Injection gaunamas prisijungiame prie duomenų bazės
    {
        _db = db;
        _raceService = raceService;
    }

    [HttpGet]
    [Authorize]
    // Šis metodas rodo vartotojo profilio puslapį su vidutiniais rezultatais ir paskutinių 20 žaidimų statistika.
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
            PastRaces = results
                .Take(20)
                .Select(r => new PastRaceViewModel
                {
                    CompletedAt = r.CompletedAt,
                    Wpm = r.WPM,
                    Accuracy = r.Accuracy,
                    ErrorCount = r.ErrorCount
                })
                .ToList(),
            Mistakes = await _raceService.GetMistakeStatsAsync(userId),
            OwnedItems = (await _db.UserItems.Where(i => i.UserId == userId).Select(i => i.ItemId).ToListAsync()).ToHashSet(),
            EquippedItems = new[]
            {
                ShopCatalog.Equipped(user.HatId, ShopCatalog.Hats).Id,
                ShopCatalog.Equipped(user.FurId, ShopCatalog.Furs).Id,
                ShopCatalog.Equipped(user.TrackId, ShopCatalog.Tracks).Id
            }.ToHashSet()
        };

        return View("Stats", model); // perudodame į /Stats puslapį
    }

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
}

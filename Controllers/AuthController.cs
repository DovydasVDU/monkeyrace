using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;


// Atsakingas už registraciją, prisijungimą ir atsijungimą

public class AuthController : Controller
{
    // Nepavykę prisijungimai pagal slapyvardį (riba pagal IP neapsaugo, kai spėliojama iš daug adresų).
    // Po 8 nepavykusių – 10 min. pertrauka. shortcut: laikoma atmintyje (viena instancija, perkrovus nunulinama);
    // keliems serveriams perkelti į DB. Kita pusė: kitas žmogus gali užrakinti svetimą slapyvardį 10 min.
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Since)> FailedLogins = new();
    private const int MaxFailedLogins = 8;
    private static readonly TimeSpan LoginLockWindow = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db; // tai kintamasis, kuris leidžia pasiekti duomenų bazę

    public AuthController(AppDbContext db)
    {
        _db = db; // per Dependency Injection gaunamas AppDbContext ir priskiriamas _db
    }
    // POST užklausa į /Auth/Register
    // Čia tikrinama, ar username ir password nėra tušti, ar username jau egzistuoja.
    // Jei viskas gerai, sukuriamas naujas User objektas, išsaugomas duomenų bazėje, ir vartotojo informacija saugoma sesijoje.
    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(string username, string password) // registracijos metodas
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) // tikrinam, ar duomenys nėra tušti
            return BadRequest(); // http 400  - failed to load request

        // Slapyvardis rodomas lentelėse, todėl leidžiame tik raides, skaičius ir _ (apsauga nuo HTML/JS įterpimo)
        if (!Regex.IsMatch(username, @"^[\p{L}\p{N}_]{3,20}$"))
            return BadRequest(new { message = "Slapyvardis: 3–20 raidžių, skaičių arba _" });
        if (password.Length < 6)
            return BadRequest(new { message = "Slaptažodis turi būti bent 6 simbolių" });

        var exists = await _db.Users.AnyAsync(u => u.Username == username);

        if (exists)
            return BadRequest(new { message = "Šis slapyvardis jau egzistuoja" });  // JSON formatu grąžinama žinutė į html

        var user = new User(); //sukuriamas naujas User objektas
        user.Username = username;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password); // slaptažodis šifruojamas naudojant BCrypt.Hash

        _db.Users.Add(user); // pridedamas į duomenų bazę

        await _db.SaveChangesAsync(); // išsaugoma

        //HttpContext.Session.SetString("UserId", user.Id.ToString()); // pasakoma sesijoje, kad toks vartotojas yra prisijungęs
        //HttpContext.Session.SetString("Username", user.Username); // gaunamas username
        await SignInUser(user); // išduodamas cookie su vartotojo informacija

        return Ok();
    }

    [HttpPost]
    [EnableRateLimiting("auth")]

    // Login metodas, kuris tikrina, ar vartotojas su tokiu username egzistuoja, ir ar slaptažodžio Hash teisingas.
    // Jei taip, saugo vartotojo informaciją sesijoje.
    public async Task<IActionResult> Login(string username, string password)
    {
       // Asinchroniškai grąžina pirmą elementą, kuris atitinka nurodytą sąlygą (u.Username == username). Jei nerandama, grąžina null.
        var failedKey = (username ?? "").Trim().ToLowerInvariant();
        if (FailedLogins.TryGetValue(failedKey, out var failed) && failed.Count >= MaxFailedLogins &&
            DateTime.UtcNow - failed.Since < LoginLockWindow)
            return StatusCode(429, new { message = "Per daug nepavykusių bandymų. Pabandyk po kelių minučių." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username); 

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            if (FailedLogins.Count > 10_000)
                FailedLogins.Clear();
            FailedLogins.AddOrUpdate(failedKey, (1, DateTime.UtcNow), (_, old) =>
                DateTime.UtcNow - old.Since >= LoginLockWindow ? (1, DateTime.UtcNow) : (old.Count + 1, old.Since));
            return Unauthorized(new { message = "Neteisingi prisijungimo duomenys" }); // JSON formatu grąžinama žinutė į html
        }
        FailedLogins.TryRemove(failedKey, out _);
        // kitu atveju prijungiamas
        //HttpContext.Session.SetString("UserId", user.Id.ToString()); // į sesiją įrašomas vartotojo ID - jis prisijungęs
        //HttpContext.Session.SetString("Username", user.Username);

        await SignInUser(user);

        return Ok();
    }

    [HttpGet] 
    public async Task<IActionResult> Logout()
    {
        // Ištrina autentifikacijos cookie
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear(); // logout metu išvaloma informacija sesijoje (LastSnippetsIds
        return Redirect("/"); // Nukreipiame į pagrindinį puslapį
    }
    // Pagalbinis metodas – sukuria Claims ir išduoda Cookie
    private async Task SignInUser(User user)
    {
        // Claims = faktai apie vartotoją įrašyti į cookie
        // Kaip ID kortelė su informacija
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name,           user.Username)
        };

        // ClaimsIdentity = visa tapatybė su visais claims
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        // ClaimsPrincipal = vartotojas su savo tapatybe
        var principal = new ClaimsPrincipal(identity);

        // Prisijungimo nustatymai
        var properties = new AuthenticationProperties
        {
            // "Prisimink mane" – cookie išlieka po naršyklės uždarymo
            IsPersistent = true,
            // Cookie gyvavimo laikas
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        // Išduodame cookie – naršyklė jį automatiškai saugos ir siųs
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }
}

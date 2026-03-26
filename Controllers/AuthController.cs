using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;


// Atsakingas už registraciją, prisijungimą ir atsijungimą

public class AuthController : Controller
{
    private readonly AppDbContext _db; // tai kintamasis, kuris leidžia pasiekti duomenų bazę

    public AuthController(AppDbContext db)
    {
        _db = db; // per Dependency Injection gaunamas AppDbContext ir priskiriamas _db
    }
    // POST užklausa į /Auth/Register
    // Čia tikrinama, ar username ir password nėra tušti, ar username jau egzistuoja.
    // Jei viskas gerai, sukuriamas naujas User objektas, išsaugomas duomenų bazėje, ir vartotojo informacija saugoma sesijoje.
    [HttpPost] 
    [IgnoreAntiforgeryToken] // dėl https saugumo meta klaidą, nes prisijungiant reikalingas šifravimas, todėl išjungiu
    public async Task<IActionResult> Register(string username, string password) // registracijos metodas
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) // tikrinam, ar duomenys nėra tušti
            return BadRequest(); // http 400  - failed to load request

        var exists = await _db.Users.AnyAsync(u => u.Username == username);

        if (exists)
            return BadRequest(new { message = "Username already exists" });  // JSON formatu grąžinama žinutė į html

        var user = new User(); //sukuriamas naujas User objektas
        user.Username = username;
        user.PasswordHash = password; // splaptažodis saugomas kaip paprastas tekstas!

        _db.Users.Add(user); // pridedama į duomenų bazę

        await _db.SaveChangesAsync(); // išsaugoma

        HttpContext.Session.SetString("UserId", user.Id.ToString()); // pasakoma sesijoje, kad toks vartotojas yra prisijungęs
        HttpContext.Session.SetString("Username", user.Username); // gaunamas username

        return Ok();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]// dėl https saugumo meta klaidą, nes prisijungiant reikalingas šifravimas, todėl išjungiu

    // Login metodas, kuris tikrina, ar vartotojas su tokiu username egzistuoja, ir ar slaptažodis teisingas.
    // Jei taip, saugo vartotojo informaciją sesijoje.
    public async Task<IActionResult> Login(string username, string password)
    {
       // Asinchroniškai grąžina pirmą elementą, kuris atitinka nurodytą sąlygą (u.Username == username). Jei nerandama, grąžina null.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username); 

        if (user == null)
            return Unauthorized(new { message = "Invalid username or password" }); // JSON formatu grąžinama žinutė į html

        if (user.PasswordHash != password)
            return Unauthorized(new { message = "Invalid username or password" });

        HttpContext.Session.SetString("UserId", user.Id.ToString()); // į sesiją įrašomas vartotojo ID - jis prisijungęs
        HttpContext.Session.SetString("Username", user.Username);

        return Ok();
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear(); // logout metu išvaloma informacija sesijoje
        return Redirect("/");
    }
}

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

    public RaceController(AppDbContext db, RaceService raceService)
    {
        _db = db;
        _raceService = raceService; // prijungiame duomenų bazę per Dependency Injection, kad galėtume saugoti rezultatus ir gauti tekstus iš duomenų bazės
    }

    public async Task<IActionResult> Play(string difficulty = "medium") // pagrindinis žaidimo puslapis, kuris parenka tekstą pagal sunkumą ir rodo jį žaidėjui
    {
        // iš sesijos gauname paskutinių parinktų tekstų Id, kad galėtume jų vengti, kai parenkame naują tekstą žaidimui (testuojant man tai nepatiko)
        var lastIdsJson = HttpContext.Session.GetString("LastSnippetIds"); 

        var lastIds = string.IsNullOrEmpty(lastIdsJson)
            ? new List<int>() // tikriname, ar sesijoje yra saugomi ID's. Jei nėra sukuriame JSON listą, jei yra, tai jį deserializuojame į List<int>
            : JsonSerializer.Deserialize<List<int>>(lastIdsJson)!;

        // paimame atsitiktinį tekstą iš duomenų bazės pagal sunkumą ir lastSnippets
        var snippet = await _raceService.GetRandomSnippetAsync(difficulty, lastIds); // čia naudojame iš raceService. _ - tai yra konvencija, kad tai yra private laukas, kuris yra naudojamas tik šioje klasėje

        // lastIds logika paprasta - saugome paskutinius tris, jei jų padaugėja - pirmąjį pašaliname
        lastIds.Add(snippet.Id);
        if (lastIds.Count > 3)
            lastIds.RemoveAt(0);

        HttpContext.Session.SetString("LastSnippetIds", JsonSerializer.Serialize(lastIds)); // atnaujiname ir išsaugom sesijoje lastIds

        // Kai atsidaro langas, sukuriame naują Race objektą. 
        var race = new Race
        {
            TextSnippetId = snippet.Id, // ištraukiame tesktą
            StartedAt = DateTime.UtcNow // datą
        };
        _db.Races.Add(race); // pridedame įrašą į duomenų bazę
        await _db.SaveChangesAsync();

        ViewBag.RaceId = race.Id; // 
        ViewBag.SelectedDifficulty = difficulty;

        return View(snippet);
    }

    public IActionResult Lobby()
    {
        return View();
    }
    // GET /Race/MultiPlay?roomId=abc123
    public IActionResult MultiPlay(string roomId)
    {
        ViewBag.RoomId = roomId;
        ViewBag.Username = HttpContext.Session.GetString("Username") ?? "Svečias";

        return View("MultiPlay"); // Return without a snippet model
    }

    [HttpPost]
    [Authorize]
    // Šis metodas priima rezultatus iš frontend'o ir saugo juos duomenų bazėje. Taip pat tikrina, ar prisijungęs vartotojas, ir priskiria rezultatą tam vartotojui.
    public async Task<IActionResult> SaveResult([FromBody] RaceResultDto dto) 
    {
        if (dto == null) 
        {
            return BadRequest("Invalid payload.");
        }

        // Pirmiausia reikia patikrinti, ar vartotojas prisijungęs
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr))
            return Unauthorized();

        // Paverčiame prisijungusio vartotojo ID į int ir pateikiame servisui
        var userId = int.Parse(userIdStr);

        await _raceService.SaveRaceResultAsync(userId, dto.RaceId, dto.Wpm, dto.Accuracy, dto.TimeSeconds);

        return Json(new { success = true }); 
    }
}
// DTO (Data Transfer Object) klasė, skirta priimti rezultatus iš frontend'o ir saugoti juos duomenų bazėje
public class RaceResultDto 
{
    public int RaceId { get; set; }
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int TimeSeconds { get; set; }
}
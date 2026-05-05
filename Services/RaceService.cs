// Services/RaceService.cs
using System;
using System.Collections.Generic;
using System.Linq; // where ir any
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; //countAsync, skip, firstAsync

// Šis service atsakingas už teksto parinkimą. 
// Renkamas pagal vartotojo pasirinktą sunkumą bei filtruojamas pagal paskutinių 3 žaidimų tekstų id, kad nesikartotų. 
public class RaceService
{
    private readonly AppDbContext _db;
    private static readonly Random _random = new Random();
    public RaceService(AppDbContext db) // prijungiame duomenų bazę
    {
        _db = db;
    }

    public async Task<TextSnippet> GetRandomSnippetAsync(List<int> lastIds)
    {
        var query = _db.TextSnippets.AsQueryable(); // kreipiamės į duomenų bazę TextSnippets lentelę

        if (lastIds.Any()) // jei egzistuoja filtruojame, kad nesikartotų paskutinių 3 žaidimų tekstai
        {
            query = query.Where(t => !lastIds.Contains(t.Id));
        }

        var count = await query.CountAsync(); // suskaičiuojame kiek yra tekstų 
        if (count == 0)
        {
            // jeigu nėra, tada nuimam filtrus
            query = _db.TextSnippets.AsQueryable();

            count = await query.CountAsync(); // vėl prašom tekstų skaičiaus
        }

        var index = _random.Next(count); // paimama random iš tų tekstų

        return await query // laukiam atsakymo
            .Skip(index) // praleidžiame tą index kiekį tekstų iš visų gautų ir paimame išrinktąjį
            .FirstAsync();
    }

    public async Task SaveRaceResultAsync(int userId, int raceId, int wpm, double accuracy, int timeSeconds)
    {
        var result = new RaceResult
        {
            UserId = userId,
            RaceId = raceId,
            WPM = wpm,
            Accuracy = accuracy,
            TimeSeconds = timeSeconds
        };

        _db.RaceResults.Add(result);

        var race = await _db.Races.FindAsync(raceId);
        if (race != null && race.Status != "finished")
        {
            race.Status = "finished";
        }

        await _db.SaveChangesAsync();
    }
}
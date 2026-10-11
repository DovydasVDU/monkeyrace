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
    // Greitesnis rezultatas laikomas sukčiavimu (pasaulio rekordai siekia ~200-300 WPM)
    public const int MaxWpm = 250;

    private readonly AppDbContext _db;
    private static readonly Random _random = new Random();
    public RaceService(AppDbContext db) // prijungiame duomenų bazę
    {
        _db = db;
    }

    public async Task<TextSnippet> GetRandomSnippetAsync(List<int> lastIds)
    {
        var query = _db.TextSnippets.AsQueryable(); // kreipiamės į duomenų bazę

        if (lastIds.Any()) 
        {
            // filtruojame, kad nesikartotų paskutiniai 3
            query = query.Where(t => !lastIds.Contains(t.Id));
        }

        var count = await query.CountAsync(); // paimame likusių skaičių (pvz., 57)

        // Ištrinta perteklinė if (count == 0) apsauga, nes žinome, kad tekstų visada liks.

        var index = _random.Next(count); // sugeneruojame atsitiktinį indeksą

        return await query
            .Skip(index)
            .FirstAsync(); // grąžiname tą konkretų tekstą
    }

    public async Task SaveRaceResultAsync(int userId, int raceId, int wpm, double accuracy, int timeSeconds, int errorCount)
    {
        var result = new RaceResult
        {
            UserId = userId,
            RaceId = raceId,
            WPM = wpm,
            Accuracy = accuracy,
            TimeSeconds = timeSeconds,
            ErrorCount = errorCount
        };

        _db.RaceResults.Add(result); // išsaugoma RaceResults lentelėje

        var race = await _db.Races.FindAsync(raceId);
        if (race != null && race.Status != "finished")
        {
            race.Status = "finished";
        }

        await _db.SaveChangesAsync();
    }
}
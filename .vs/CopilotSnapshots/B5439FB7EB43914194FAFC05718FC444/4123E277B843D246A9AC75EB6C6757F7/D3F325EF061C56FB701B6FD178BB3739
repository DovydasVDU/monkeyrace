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

    public async Task<TextSnippet> GetRandomSnippetAsync(
        string difficulty,
        List<int> lastIds)
    {
        var query = _db.TextSnippets // kreipiamės į duomenų bazę TextSnippets lentelę, filtruojame pagal sunkumą
            .Where(t => t.Difficulty == difficulty);

        if (lastIds.Any()) // jei egzistuoja filtruojame, kad nesikartotų paskutinių 3 žaidimų tekstai
        {
            query = query.Where(t => !lastIds.Contains(t.Id));
        }

        var count = await query.CountAsync(); // suskaičiuojame kiek yra tekstų 
        if (count == 0)
        {
            // jeigu nėra, tada nuimam filtrus
            query = _db.TextSnippets
                .Where(t => t.Difficulty == difficulty);

            count = await query.CountAsync(); // vėl prašom tekstų skaičiaus
        }

        var index = _random.Next(count); // paimama random iš tų tekstų

        return await query // laukiam atsakymo
            .Skip(index) // praleidžiame tą index kiekį tekstų iš visų gautų ir paimame išrinktąjį
            .FirstAsync();
    }
}
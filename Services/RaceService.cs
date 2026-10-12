// Services/RaceService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // where ir any
using System.Threading;
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

    // shortcut: užraktas gyvena procese, todėl tinka vienam serveriui. Keliems serveriams reikėtų DB lygio užrakto.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _userLocks =
        new ConcurrentDictionary<int, SemaphoreSlim>();

    // Vienintelė rezultatų įrašymo vieta (solo ir multiplayer). Čia pat skiriamas XP ir bananai.
    // Jei rezultatas šioms lenktynėms jau įrašytas, nieko nepridedama ir grąžinamas ankstesnis apdovanojimas.
    public async Task<RaceReward> SaveRaceResultAsync(int userId, int raceId, RaceMode mode, int? place,
        int wpm, double accuracy, int timeSeconds, int errorCount)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var user = await _db.Users.FirstAsync(u => u.Id == userId);

            var existing = await _db.RaceResults
                .FirstOrDefaultAsync(r => r.RaceId == raceId && r.UserId == userId);
            if (existing != null)
                return RaceReward.Create(user.Xp - existing.XpGained, user.Xp, user.Bananas,
                    existing.XpGained, existing.BananasGained, firstRace: false, saved: true);

            var isFirstRace = !await _db.RaceResults.AnyAsync(r => r.UserId == userId);
            var race = await _db.Races.FindAsync(raceId);
            var reward = Progression.RewardFor(mode, place, wpm, accuracy,
                finished: true, isFirstRace, user.Xp, race?.DurationSeconds);

            _db.RaceResults.Add(new RaceResult
            {
                UserId = userId,
                RaceId = raceId,
                WPM = wpm,
                Accuracy = accuracy,
                TimeSeconds = timeSeconds,
                ErrorCount = errorCount,
                XpGained = reward.Xp,
                BananasGained = reward.Bananas
            });

            var xpBefore = user.Xp;
            user.Xp += reward.Xp;
            user.Bananas += reward.Bananas;

            if (race != null && race.Status != "finished")
                race.Status = "finished";

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return RaceReward.Create(xpBefore, user.Xp, user.Bananas,
                reward.Xp, reward.Bananas, isFirstRace, saved: true);
        }
        finally
        {
            userLock.Release();
        }
    }

    // Viena vieta žaidėjo statistikai: /api/stats/{username}, navbar, pagrindinis puslapis ir lenktynės
    public async Task<PlayerCard> GetPlayerCardAsync(int userId)
    {
        var user = await _db.Users.Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.Xp, u.Bananas })
            .FirstOrDefaultAsync();
        if (user == null)
            return null;

        var results = _db.RaceResults.Where(r => r.UserId == userId);
        var level = Progression.LevelFor(user.Xp);
        return new PlayerCard
        {
            Username = user.Username,
            Level = level,
            Xp = user.Xp,
            XpToNext = Progression.ToNextLevel(user.Xp),
            LevelProgress = Progression.LevelProgress(user.Xp),
            Bananas = user.Bananas,
            TotalRaces = await results.CountAsync(),
            BestWpm = await results.MaxAsync(r => (int?)r.WPM) ?? 0,
            AverageAccuracy = Math.Round(await results.AverageAsync(r => (double?)r.Accuracy) ?? 0, 2)
        };
    }

    // Svečiui: kiek būtų gavęs, jei būtų prisijungęs. Niekur nesaugoma.
    public static RaceReward GuestReward(RaceMode mode, int? place, int wpm, double accuracy,
        int? durationSeconds = null)
    {
        var reward = Progression.RewardFor(mode, place, wpm, accuracy,
            finished: true, isFirstRace: false, xpBefore: 0, durationSeconds);
        return RaceReward.Create(0, reward.Xp, reward.Bananas,
            reward.Xp, reward.Bananas, firstRace: false, saved: false);
    }
}

// Vieša žaidėjo statistika (be el. pašto ar kitų slaptų duomenų)
public sealed class PlayerCard
{
    public string Username { get; init; }
    public int Level { get; init; }
    public int Xp { get; init; }
    public int? XpToNext { get; init; }
    public double LevelProgress { get; init; }
    public int Bananas { get; init; }
    public int TotalRaces { get; init; }
    public int BestWpm { get; init; }
    public double AverageAccuracy { get; init; }
}

// Apdovanojimas, siunčiamas į naršyklę (solo SaveResult JSON ir multiplayer RaceFinished)
public sealed class RaceReward
{
    public int XpGained { get; init; }
    public int BananasGained { get; init; }
    public int Level { get; init; }
    public bool LeveledUp { get; init; }
    public int Xp { get; init; }
    public int? XpToNext { get; init; }
    public double LevelProgressBefore { get; init; }
    public double LevelProgress { get; init; }
    public int Bananas { get; init; }
    public bool FirstRace { get; init; }
    // false = svečio hipotetinis apdovanojimas
    public bool Saved { get; init; }

    public static RaceReward Create(int xpBefore, int xp, int bananas,
        int xpGained, int bananasGained, bool firstRace, bool saved)
    {
        var level = Progression.LevelFor(xp);
        return new RaceReward
        {
            XpGained = xpGained,
            BananasGained = bananasGained,
            Level = level,
            LeveledUp = level > Progression.LevelFor(xpBefore),
            Xp = xp,
            XpToNext = Progression.ToNextLevel(xp),
            LevelProgressBefore = Progression.LevelProgress(xpBefore),
            LevelProgress = Progression.LevelProgress(xp),
            Bananas = bananas,
            FirstRace = firstRace,
            Saved = saved
        };
    }
}

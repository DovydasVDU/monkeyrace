// Services/RaceService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq; // where ir any
using System.Text.RegularExpressions;
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
    public RaceService(AppDbContext db) // prijungiame duomenų bazę
    {
        _db = db;
    }

    public async Task<TextSnippet> GetRandomSnippetAsync(List<int> lastIds)
    {
        // tik importuoti tekstai (su autoriumi ir šaltiniu); seni seed tekstai lieka DB dėl senų Race įrašų
        var query = _db.TextSnippets.Where(t => t.SourceId != null); // kreipiamės į duomenų bazę

        if (lastIds.Any()) 
        {
            // filtruojame, kad nesikartotų paskutiniai 3
            query = query.Where(t => !lastIds.Contains(t.Id));
        }

        var count = await query.CountAsync(); // paimame likusių skaičių (pvz., 57)

        // Ištrinta perteklinė if (count == 0) apsauga, nes žinome, kad tekstų visada liks.

        var index = Random.Shared.Next(count); // sugeneruojame atsitiktinį indeksą

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
                Place = place,
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

    // Po lenktynių naršyklės atsiųstos klaidos (POST /race/mistakes). Apdovanojimų neduoda, todėl
    // netikri duomenys sugadintų tik paties žaidėjo statistiką.
    // shortcut: eilučių skaičius vienam žaidėjui neribojamas (kiekvienas naujas žodis – eilutė); jei kas pradėtų
    // tuo piktnaudžiauti, trinti seniausius pagal LastSeenAt.
    public async Task SaveMistakesAsync(int userId, MistakesDto dto)
    {
        var keys = dto.Keys
            .Where(key => key.Expected?.Length == 1 && key.Typed?.Length == 1 && key.Count > 0)
            .GroupBy(key => (key.Expected, key.Typed))
            .ToDictionary(group => group.Key, group => group.Sum(key => key.Count));
        var words = dto.Words
            .Select(word => (Word: NormalizeWord(word.Word), word.HadError))
            .Where(word => word.Word.Length is > 0 and <= MistakesDto.MaxWordLength)
            .ToList();
        var distinctWords = words.Select(word => word.Word).Distinct().ToList();

        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            var keyRows = await _db.UserKeyStats.Where(k => k.UserId == userId)
                .ToDictionaryAsync(k => (k.Expected, k.Typed));
            foreach (var ((expected, typed), count) in keys)
            {
                if (!keyRows.TryGetValue((expected, typed), out var row))
                    _db.UserKeyStats.Add(row = new UserKeyStat { UserId = userId, Expected = expected, Typed = typed });
                row.Count += count;
            }

            var wordRows = await _db.UserWordMistakes
                .Where(w => w.UserId == userId && distinctWords.Contains(w.Word))
                .ToDictionaryAsync(w => w.Word);
            // eilės tvarka svarbi: tas pats žodis tekste gali būti kelis kartus
            foreach (var (word, hadError) in words)
            {
                wordRows.TryGetValue(word, out var row);
                if (!hadError)
                {
                    if (row != null)
                        row.CorrectStreak++;
                    continue;
                }
                if (row == null)
                    _db.UserWordMistakes.Add(wordRows[word] = row = new UserWordMistake { UserId = userId, Word = word });
                row.Misses++;
                row.CorrectStreak = 0;
                row.LastSeenAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }
        finally
        {
            userLock.Release();
        }
    }

    // Raidė rodoma tik po tiek bandymų, kitaip procentas atsitiktinis
    public const int MinLetterAttempts = 20;
    // Žodis laikomas išmoktu, kai tiek kartų iš eilės parašytas be klaidų (treniruoklis jo nebesiūlys)
    public const int MasteredStreak = 3;

    // Profilio „Klaidų analizė“
    public async Task<MistakeStats> GetMistakeStatsAsync(int userId)
    {
        var keys = await _db.UserKeyStats.Where(k => k.UserId == userId).ToListAsync();
        var attempts = keys.Sum(k => k.Count);
        var misses = keys.Where(k => k.Typed != k.Expected).Sum(k => k.Count);

        var confusions = keys.Where(k => k.Typed != k.Expected)
            .OrderByDescending(k => k.Count)
            .Take(8)
            .Select(k => new LetterConfusion { Expected = k.Expected, Typed = k.Typed, Count = k.Count })
            .ToList();

        var words = _db.UserWordMistakes.Where(w => w.UserId == userId);
        return new MistakeStats
        {
            Attempts = attempts,
            Accuracy = attempts == 0 ? 100 : (attempts - misses) * 100d / attempts,
            WeakLetters = WeakLetters(keys).Take(8).ToList(),
            Confusions = confusions,
            Words = await words.Where(w => w.CorrectStreak < MasteredStreak)
                .OrderByDescending(w => w.Misses).ThenByDescending(w => w.LastSeenAt)
                .Take(10).ToListAsync(),
            MasteredWords = await words.CountAsync(w => w.CorrectStreak >= MasteredStreak)
        };
    }

    // „Š“ ir „š“ – ta pati raidė; klaida skaičiuojama ir tada, kai tik pamiršta Shift
    private static IEnumerable<LetterStat> WeakLetters(IEnumerable<UserKeyStat> keys) =>
        keys.GroupBy(k => k.Expected.ToLowerInvariant())
            .Select(g => new LetterStat
            {
                Letter = g.Key,
                Attempts = g.Sum(k => k.Count),
                Misses = g.Where(k => k.Typed != k.Expected).Sum(k => k.Count)
            })
            .Where(l => l.Attempts >= MinLetterAttempts && l.Misses > 0)
            .OrderByDescending(l => l.MissPercent).ThenByDescending(l => l.Misses);

    // Treniruoklis (/race/train): kiekvienas neišmoktas žodis du kartus + žodžiai su silpniausiomis raidėmis.
    // null – dar nėra ką treniruoti.
    public async Task<string> BuildTrainingTextAsync(int userId)
    {
        var words = await _db.UserWordMistakes
            .Where(w => w.UserId == userId && w.CorrectStreak < MasteredStreak)
            .OrderByDescending(w => w.Misses).ThenByDescending(w => w.LastSeenAt)
            .Take(15)
            .Select(w => w.Word)
            .ToListAsync();
        var keys = await _db.UserKeyStats.Where(k => k.UserId == userId).ToListAsync();
        var letters = WeakLetters(keys).Select(l => l.Letter).Where(l => char.IsLetter(l[0])).Take(3).ToList();

        var practice = WordService.WithLetters(letters, Math.Max(10, 30 - words.Count * 2));
        var text = words.Concat(words).Concat(practice).ToArray();
        if (text.Length == 0)
            return null;
        Random.Shared.Shuffle(text);
        return string.Join(" ", text);
    }

    // „Vakarykštę.“ ir „vakarykštę“ – tas pats žodis
    private static string NormalizeWord(string word) =>
        Regex.Replace((word ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}-]", "").Trim('-');

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

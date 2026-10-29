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
    public async Task<LessonSummary> GetLessonSummaryAsync(int userId)
    {
        var rows = await _db.UserLessonProgresses.Where(p => p.UserId == userId)
            .Select(p => new { p.LessonId, p.Stars }).ToListAsync();
        return LessonCatalog.Summarize(rows.Select(p => (p.LessonId, p.Stars)));
    }

    public async Task<PlayerCard> GetPlayerCardAsync(int userId)
    {
        var user = await _db.Users.Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.Xp, u.Bananas, u.HatId, u.FurId, u.TrackId })
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
            // kaip profilyje (UserController.Stats): visų rezultatų vidurkis, nupjautas
            AverageWpm = (int)(await results.AverageAsync(r => (double?)r.WPM) ?? 0),
            AverageAccuracy = Math.Round(await results.AverageAsync(r => (double?)r.Accuracy) ?? 0, 2),
            HatColor = ShopCatalog.Equipped(user.HatId, ShopCatalog.Hats).Value,
            FurColor = ShopCatalog.Equipped(user.FurId, ShopCatalog.Furs).Value,
            Track = ShopCatalog.Equipped(user.TrackId, ShopCatalog.Tracks).Value
        };
    }

    // Parduotuvė: neturimą prekę nuperka (jei užtenka bananų) ir užsideda; turimą – tik užsideda.
    // null – pavyko, kitaip klaidos tekstas žaidėjui. Tas pats užraktas kaip SaveRaceResultAsync,
    // kad pirkimas ir lenktynių bananai vienu metu neperrašytų vienas kito.
    public async Task<string> EquipItemAsync(int userId, string itemId)
    {
        var item = ShopCatalog.Find(itemId);
        if (item == null)
            return "Tokios prekės nėra.";

        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            var user = await _db.Users.FirstAsync(u => u.Id == userId);
            var owned = item.Price == 0 ||
                await _db.UserItems.AnyAsync(i => i.UserId == userId && i.ItemId == item.Id);
            if (!owned)
            {
                if (user.Bananas < item.Price)
                    return $"Trūksta {item.Price - user.Bananas} bananų.";
                user.Bananas -= item.Price;
                _db.UserItems.Add(new UserItem { UserId = userId, ItemId = item.Id });
            }

            switch (item.Category)
            {
                case ShopCatalog.Hats: user.HatId = item.Id; break;
                case ShopCatalog.Furs: user.FurId = item.Id; break;
                default: user.TrackId = item.Id; break;
            }
            await _db.SaveChangesAsync();
            return null;
        }
        finally
        {
            userLock.Release();
        }
    }

    // Pamokos rezultatas: išsaugo geriausią rezultatą ir apdovanoja tik už naujas žvaigždutes.
    // Tas pats užraktas kaip SaveRaceResultAsync, kad XP ir bananai nepasimestų lygiagrečiai.
    public async Task<LessonSaveResult> SaveLessonResultAsync(int userId, string lessonId, int stars,
        double accuracy, int wpm)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var user = await _db.Users.FirstAsync(u => u.Id == userId);
            var progress = await _db.UserLessonProgresses
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId);
            if (progress == null)
                _db.UserLessonProgresses.Add(progress = new UserLessonProgress { UserId = userId, LessonId = lessonId });

            var previousStars = progress.Stars;
            var reward = Progression.LessonReward(stars, previousStars, user.Xp);
            var xpBefore = user.Xp;
            user.Xp += reward.Xp;
            user.Bananas += reward.Bananas;

            progress.Attempts++;
            progress.Stars = Math.Max(progress.Stars, stars);
            progress.BestAccuracy = Math.Max(progress.BestAccuracy, accuracy);
            progress.BestWpm = Math.Max(progress.BestWpm, wpm);
            progress.UpdatedAt = DateTime.UtcNow;
            if (stars > 0 && progress.CompletedAt == null)
                progress.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new LessonSaveResult
            {
                Stars = stars,
                BestStars = progress.Stars,
                NewStars = Math.Max(0, stars - previousStars),
                Reward = RaceReward.Create(xpBefore, user.Xp, user.Bananas, reward.Xp, reward.Bananas,
                    firstRace: false, saved: true)
            };
        }
        finally
        {
            userLock.Release();
        }
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
    public int AverageWpm { get; init; }
    public double AverageAccuracy { get; init; }
    // Parduotuvės kosmetika (ShopCatalog Value): beždžionė navbar, profilyje ir lenktynių trasoje
    public string HatColor { get; init; }
    public string FurColor { get; init; }
    public string Track { get; init; }

    // Kitiems žaidėjams (API, lenktynių kambarys) – tik tai, kas rodoma kortelėje: be XP, bananų ir lenktynių skaičiaus
    public PublicPlayerCard ForOthers() => new()
    {
        Username = Username,
        Level = Level,
        LevelProgress = LevelProgress,
        BestWpm = BestWpm,
        AverageWpm = AverageWpm,
        AverageAccuracy = AverageAccuracy,
        HatColor = HatColor,
        FurColor = FurColor,
        Track = Track
    };
}

public sealed class PublicPlayerCard
{
    public string Username { get; init; }
    public int Level { get; init; }
    public double LevelProgress { get; init; }
    public int BestWpm { get; init; }
    public int AverageWpm { get; init; }
    public double AverageAccuracy { get; init; }
    public string HatColor { get; init; }
    public string FurColor { get; init; }
    public string Track { get; init; }
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

// Pamokos išsaugojimo atsakymas naršyklei
public sealed class LessonSaveResult
{
    // šio bandymo žvaigždutės
    public int Stars { get; init; }
    // geriausias rezultatas po šio bandymo
    public int BestStars { get; init; }
    // kiek žvaigždučių gauta pirmą kartą (už jas duotas apdovanojimas)
    public int NewStars { get; init; }
    public RaceReward Reward { get; init; }
}

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
            var newItems = await SettleLevelsAsync(user, payBananas: true); // senų lygių skolos (po 100 lygių išplėtimo)
            user.Xp += reward.Xp;
            user.Bananas += reward.Bananas;
            newItems.AddRange(await SettleLevelsAsync(user, payBananas: false)); // bananus už lygį jau įskaičiavo RewardFor

            if (race != null && race.Status != "finished")
                race.Status = "finished";

            await _db.SaveChangesAsync();
            var achievements = await SettleAchievementsAsync(user, newItems);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return RaceReward.Create(xpBefore, user.Xp, user.Bananas,
                reward.Xp + achievements.Xp, reward.Bananas, isFirstRace, saved: true, newItems, achievements.Notes);
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
            .Select(u => new { u.Username, u.Xp, u.Bananas, u.HatId, u.FurId, u.TrackId, u.BackgroundId, u.FrameId })
            .FirstOrDefaultAsync();
        if (user == null)
            return null;

        var results = _db.RaceResults.Where(r => r.UserId == userId);
        var level = Progression.LevelFor(user.Xp);
        return new PlayerCard
        {
            Username = user.Username,
            Level = level,
            Title = Progression.TitleFor(level),
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
            Track = ShopCatalog.Equipped(user.TrackId, ShopCatalog.Tracks).Value,
            BackgroundColor = ShopCatalog.Equipped(user.BackgroundId, ShopCatalog.Backgrounds).Value,
            FrameColor = ShopCatalog.Equipped(user.FrameId, ShopCatalog.Frames).Value
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
            var owned = item.IsDefault ||
                await _db.UserItems.AnyAsync(i => i.UserId == userId && i.ItemId == item.Id);
            if (!owned)
            {
                if (!item.IsPurchasable)
                    return $"Šio daikto nusipirkti negalima. {item.Unlock}".Trim();
                if (user.Bananas < item.Price)
                    return $"Trūksta {item.Price - user.Bananas} bananų.";
                user.Bananas -= item.Price;
                _db.UserItems.Add(new UserItem { UserId = userId, ItemId = item.Id });
            }

            switch (item.Category)
            {
                case ShopCatalog.Hats: user.HatId = item.Id; break;
                case ShopCatalog.Furs: user.FurId = item.Id; break;
                case ShopCatalog.Backgrounds: user.BackgroundId = item.Id; break;
                case ShopCatalog.Frames: user.FrameId = item.Id; break;
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
            var newItems = await SettleLevelsAsync(user, payBananas: true);
            user.Xp += reward.Xp;
            user.Bananas += reward.Bananas;
            newItems.AddRange(await SettleLevelsAsync(user, payBananas: false));

            progress.Attempts++;
            progress.Stars = Math.Max(progress.Stars, stars);
            progress.BestAccuracy = Math.Max(progress.BestAccuracy, accuracy);
            progress.BestWpm = Math.Max(progress.BestWpm, wpm);
            progress.UpdatedAt = DateTime.UtcNow;
            if (stars > 0 && progress.CompletedAt == null)
                progress.CompletedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            var path = await SettleLessonPathAsync(user);
            newItems.AddRange(path.Items);
            await _db.SaveChangesAsync();
            var achievements = await SettleAchievementsAsync(user, newItems);
            path.Notes.AddRange(achievements.Notes);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return new LessonSaveResult
            {
                Stars = stars,
                BestStars = progress.Stars,
                NewStars = Math.Max(0, stars - previousStars),
                Reward = RaceReward.Create(xpBefore, user.Xp, user.Bananas, reward.Xp + achievements.Xp, reward.Bananas + path.Bananas,
                    firstRace: false, saved: true, newItems, path.Notes)
            };
        }
        finally
        {
            userLock.Release();
        }
    }

    // Lygių prizai. ClaimedLevel – iki kurio lygio jau išduoti bananai. payBananas: true – sumoka bananus už
    // praleistus lygius (kai lygį pakėlė XP kreivės pakeitimas, o ne šios lenktynės); false – tik užfiksuoja lygį,
    // nes Progression.RewardFor bananus už kylant lygiui jau įskaičiavo. Daiktų prizai išduodami visada, jei dar
    // nepriklauso. Kviesti po transakcijos pradžios, užrakinus vartotoją; SaveChanges daro kviečiantysis.
    // Grąžina naujai išduotus daiktus.
    public async Task<List<ShopItem>> SettleLevelsAsync(User user, bool payBananas)
    {
        var level = Progression.LevelFor(user.Xp);
        if (payBananas)
            for (var l = user.ClaimedLevel + 1; l <= level; l++)
                user.Bananas += Progression.LevelUpBananas(l);
        user.ClaimedLevel = Math.Max(user.ClaimedLevel, level);

        return await GrantItemsAsync(user.Id,
            ShopCatalog.Items.Where(i => i.Source == ItemSource.Level && i.Level <= level).ToList());
    }

    // Įrašo į UserItems tik tuos daiktus, kurių žaidėjas dar neturi (įskaitant dar neišsaugotus). SaveChanges daro kviečiantysis.
    private async Task<List<ShopItem>> GrantItemsAsync(int userId, List<ShopItem> due)
    {
        if (due.Count == 0)
            return new List<ShopItem>();
        var dueIds = due.Select(i => i.Id).ToList();
        var owned = (await _db.UserItems.Where(i => i.UserId == userId && dueIds.Contains(i.ItemId))
            .Select(i => i.ItemId).ToListAsync()).ToHashSet();
        // dar neišsaugoti (tos pačios operacijos pirmas SettleLevels kvietimas)
        owned.UnionWith(_db.ChangeTracker.Entries<UserItem>()
            .Where(e => e.State == EntityState.Added && e.Entity.UserId == userId).Select(e => e.Entity.ItemId));
        var added = due.Where(i => !owned.Contains(i.Id)).ToList();
        foreach (var item in added)
            _db.UserItems.Add(new UserItem { UserId = userId, ItemId = item.Id });
        return added;
    }

    // Pamokų kelias: bananai už baigtą modulį / visas 3 žvaigždutes ir prizai už įveiktų pamokų skaičių.
    // Kiekvienas prizas vienkartinis (UserClaims). Kviesti po to, kai pamokos progresas jau išsaugotas (SaveChanges),
    // viduje to paties vartotojo užrakto ir transakcijos. Bananai pridedami prie user.Bananas.
    private async Task<(int Bananas, List<ShopItem> Items, List<string> Notes)> SettleLessonPathAsync(User user)
    {
        var stars = (await _db.UserLessonProgresses.Where(p => p.UserId == user.Id)
            .Select(p => new { p.LessonId, p.Stars }).ToListAsync()).ToDictionary(p => p.LessonId, p => p.Stars);
        var claimed = (await _db.UserClaims.Where(c => c.UserId == user.Id).Select(c => c.Key).ToListAsync()).ToHashSet();
        var bananas = 0;
        var notes = new List<string>();
        var items = new List<ShopItem>();

        bool Claim(string key, int amount, string note)
        {
            if (!claimed.Add(key))
                return false;
            _db.UserClaims.Add(new UserClaim { UserId = user.Id, Key = key });
            bananas += amount;
            notes.Add($"{note}: +{amount} bananų");
            return true;
        }

        int Stars(Lesson lesson) => stars.TryGetValue(lesson.Id, out var s) ? s : 0;
        foreach (var module in LessonCatalog.Modules)
        {
            if (module.Lessons.All(l => Stars(l) > 0))
                Claim($"module:{module.Id}", LessonPath.ModuleBananas, $"Modulis „{module.Title}“ baigtas");
            if (module.Lessons.All(l => Stars(l) == 3))
                Claim($"mastery:{module.Id}", LessonPath.MasteryBananas, $"Modulis „{module.Title}“ su trimis žvaigždutėmis");
        }

        var done = stars.Values.Count(s => s > 0);
        foreach (var milestone in LessonPath.Milestones.Where(m => done >= m.Lessons))
        {
            Claim($"milestone:{milestone.Lessons}", milestone.Bananas, $"{milestone.Lessons} pamokų įveikta");
            items.AddRange(await GrantItemsAsync(user.Id, new List<ShopItem> { ShopCatalog.Find(milestone.ItemId) }));
        }

        user.Bananas += bananas;
        return (bananas, items, notes);
    }

    // Pasiekimai: išduoda XP už naujai įvykdytus (vienkartinis, UserClaims "ach:..."), po to sumoka bananus už lygius,
    // kuriuos tas XP galėjo pakelti, ir išduoda jų prizus (newItems papildomas). Kviesti, kai lenktynių / pamokos
    // duomenys jau išsaugoti (SaveChanges), po to SaveChanges.
    private async Task<(int Xp, List<string> Notes)> SettleAchievementsAsync(User user, List<ShopItem> newItems)
    {
        var stats = await LoadAchievementStatsAsync(user);
        var claimed = (await _db.UserClaims.Where(c => c.UserId == user.Id && c.Key.StartsWith(Achievements.ClaimPrefix))
            .Select(c => c.Key).ToListAsync()).ToHashSet();
        var xp = 0;
        var notes = new List<string>();
        foreach (var achievement in Achievements.All)
        {
            var key = Achievements.ClaimPrefix + achievement.Key;
            if (claimed.Contains(key) || !achievement.IsDone(stats))
                continue;
            _db.UserClaims.Add(new UserClaim { UserId = user.Id, Key = key });
            xp += achievement.Xp;
            notes.Add($"Pasiekimas „{achievement.Title}“: +{achievement.Xp} XP");
        }
        if (xp > 0)
        {
            user.Xp += xp;
            newItems.AddRange(await SettleLevelsAsync(user, payBananas: true));
        }
        return (xp, notes);
    }

    private async Task<AchievementStats> LoadAchievementStatsAsync(User user)
    {
        var results = _db.RaceResults.Where(r => r.UserId == user.Id);
        var lastFive = await results.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.Id)
            .Take(5).Select(r => r.Accuracy).ToListAsync();
        var stars = (await _db.UserLessonProgresses.Where(p => p.UserId == user.Id)
            .Select(p => new { p.LessonId, p.Stars }).ToListAsync()).ToDictionary(p => p.LessonId, p => p.Stars);
        int Stars(Lesson lesson) => stars.TryGetValue(lesson.Id, out var s) ? s : 0;
        var owned = (await _db.UserItems.Where(i => i.UserId == user.Id).Select(i => i.ItemId).ToListAsync())
            .Select(ShopCatalog.Find).Where(i => i != null).ToList();

        return new AchievementStats
        {
            Races = await results.CountAsync(),
            BestWpm = await results.MaxAsync(r => (int?)r.WPM) ?? 0,
            PerfectRace = await results.AnyAsync(r => r.Accuracy >= 100),
            LastFiveAccurate = lastFive.Count == 5 && lastFive.All(a => a >= 98),
            OnlineWins = await results.CountAsync(r => r.Place == 1 && r.Race.IsMultiplayer),
            LessonsDone = stars.Values.Count(s => s > 0),
            ThreeStarLessons = stars.Values.Count(s => s == 3),
            ModulesDone = LessonCatalog.Modules.Count(m => m.Lessons.All(l => Stars(l) > 0)),
            Level = Progression.LevelFor(user.Xp),
            BoughtSomething = owned.Any(i => i.IsPurchasable),
            OwnedItems = owned.Count,
            CategoriesOwned = owned.Select(i => i.Category).Distinct().Count()
        };
    }

    // Profilio atidarymas: išduoda lygių prizus, kurių žaidėjas jau nusipelnė, bet dar negavo (pvz. po XP kreivės pakeitimo)
    public async Task SettleLevelsForUserAsync(int userId)
    {
        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync();
        try
        {
            var user = await _db.Users.FirstAsync(u => u.Id == userId);
            await SettleLevelsAsync(user, payBananas: true);
            await _db.SaveChangesAsync();
            await SettleAchievementsAsync(user, new List<ShopItem>()); // senų žaidėjų pasiekimai, kurie jau seniai įvykdyti
            await _db.SaveChangesAsync();
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
    public string Title { get; init; }
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
    public string BackgroundColor { get; init; }
    public string FrameColor { get; init; }

    // Kitiems žaidėjams (API, lenktynių kambarys) – tik tai, kas rodoma kortelėje: be XP, bananų ir lenktynių skaičiaus
    public PublicPlayerCard ForOthers() => new()
    {
        Username = Username,
        Level = Level,
        Title = Title,
        LevelProgress = LevelProgress,
        BestWpm = BestWpm,
        AverageWpm = AverageWpm,
        AverageAccuracy = AverageAccuracy,
        HatColor = HatColor,
        FurColor = FurColor,
        Track = Track,
        BackgroundColor = BackgroundColor,
        FrameColor = FrameColor
    };
}

public sealed class PublicPlayerCard
{
    public string Username { get; init; }
    public int Level { get; init; }
    public string Title { get; init; }
    public double LevelProgress { get; init; }
    public int BestWpm { get; init; }
    public int AverageWpm { get; init; }
    public double AverageAccuracy { get; init; }
    public string HatColor { get; init; }
    public string FurColor { get; init; }
    public string Track { get; init; }
    public string BackgroundColor { get; init; }
    public string FrameColor { get; init; }
}

// Apdovanojimas, siunčiamas į naršyklę (solo SaveResult JSON ir multiplayer RaceFinished)
public sealed class RaceReward
{
    public int XpGained { get; init; }
    public int BananasGained { get; init; }
    public int Level { get; init; }
    public string Title { get; init; }
    // ne null tik kai titulas pasikeitė (pvz. 10 -> 11 lygis: „Pirmokas“)
    public string NewTitle { get; init; }
    public bool LeveledUp { get; init; }
    public int Xp { get; init; }
    public int? XpToNext { get; init; }
    public double LevelProgressBefore { get; init; }
    public double LevelProgress { get; init; }
    public int Bananas { get; init; }
    public bool FirstRace { get; init; }
    // false = svečio hipotetinis apdovanojimas
    public bool Saved { get; init; }
    // naujai atrakinti lygių prizai (kad naršyklė parodytų „Gavai: ...“)
    public List<NewItem> NewItems { get; init; } = new();
    // papildomi bonusai (pamokų kelias): „Modulis ... baigtas: +40 bananų“
    public List<string> Notes { get; init; } = new();

    public sealed record NewItem(string Id, string Name, string Category, string Rarity);

    public static RaceReward Create(int xpBefore, int xp, int bananas,
        int xpGained, int bananasGained, bool firstRace, bool saved, IEnumerable<ShopItem> newItems = null, List<string> notes = null)
    {
        var level = Progression.LevelFor(xp);
        var title = Progression.TitleFor(level);
        return new RaceReward
        {
            XpGained = xpGained,
            BananasGained = bananasGained,
            Level = level,
            Title = title,
            NewTitle = title != Progression.TitleFor(Progression.LevelFor(xpBefore)) ? title : null,
            LeveledUp = level > Progression.LevelFor(xpBefore),
            Xp = xp,
            XpToNext = Progression.ToNextLevel(xp),
            LevelProgressBefore = Progression.LevelProgress(xpBefore),
            LevelProgress = Progression.LevelProgress(xp),
            Bananas = bananas,
            FirstRace = firstRace,
            Saved = saved,
            NewItems = (newItems ?? Enumerable.Empty<ShopItem>())
                .Select(i => new NewItem(i.Id, i.Name, i.Category, i.Rarity)).ToList(),
            Notes = notes ?? new List<string>()
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

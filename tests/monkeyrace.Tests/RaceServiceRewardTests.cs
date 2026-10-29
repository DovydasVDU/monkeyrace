using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Tikra SQLite DB faile, kad būtų patikrintos ir migracijos, ir lygiagretus įrašymas
public sealed class RaceServiceRewardTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-test-{Guid.NewGuid():N}.sqlite");

    private AppDbContext NewContext() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}").Options);

    // Pasiekimai duoda papildomo XP, todėl skaičiavimo testuose jie pažymimi kaip jau gauti (jų pačių testai – claimAchievements: false)
    internal static void ClaimAllAchievements(AppDbContext db, int userId)
    {
        db.UserClaims.AddRange(Achievements.All.Select(a =>
            new UserClaim { UserId = userId, Key = Achievements.ClaimPrefix + a.Key }));
        db.SaveChanges();
    }

    private (int UserId, int[] RaceIds) Seed(int races, bool claimAchievements = true)
    {
        using var db = NewContext();
        db.Database.Migrate(); // migracijos tuščiai DB
        var user = new User { Username = "tester", PasswordHash = "x" };
        db.Users.Add(user);
        var raceList = Enumerable.Range(0, races)
            .Select(_ => new Race { TextSnippetId = 1, StartedAt = DateTime.UtcNow, IsMultiplayer = true })
            .ToList();
        db.Races.AddRange(raceList);
        db.SaveChanges();
        if (claimAchievements)
            ClaimAllAchievements(db, user.Id);
        return (user.Id, raceList.Select(race => race.Id).ToArray());
    }

    private async Task<RaceReward> Save(int userId, int raceId, int place)
    {
        using var db = NewContext();
        return await new RaceService(db).SaveRaceResultAsync(
            userId, raceId, RaceMode.Multiplayer, place, 60, 100, 30, 0);
    }

    private (int Xp, int Bananas, int Results) UserState(int userId)
    {
        using var db = NewContext();
        var user = db.Users.Single(u => u.Id == userId);
        return (user.Xp, user.Bananas, db.RaceResults.Count(r => r.UserId == userId));
    }

    [Fact]
    public async Task FirstRaceBonus_OnlyOnce_AndRewardPersisted()
    {
        var (userId, raceIds) = Seed(2);

        var first = await Save(userId, raceIds[0], 4);
        var second = await Save(userId, raceIds[1], 4);

        Assert.True(first.FirstRace);
        Assert.False(second.FirstRace);
        Assert.Equal(second.XpGained + 50, first.XpGained);
        Assert.Equal(first.XpGained + second.XpGained, UserState(userId).Xp);
        Assert.Equal(first.BananasGained + second.BananasGained, UserState(userId).Bananas);
    }

    [Fact]
    public async Task SavingSameResultTwice_GivesNoExtraXp()
    {
        var (userId, raceIds) = Seed(1);

        var first = await Save(userId, raceIds[0], 1);
        var again = await Save(userId, raceIds[0], 1);

        Assert.Equal(first.XpGained, again.XpGained);
        var state = UserState(userId);
        Assert.Equal(first.XpGained, state.Xp);
        Assert.Equal(1, state.Results);
    }

    [Fact]
    public async Task ConcurrentSaves_AddUpCorrectly()
    {
        var (userId, raceIds) = Seed(6);

        var rewards = await Task.WhenAll(raceIds.Select(raceId => Task.Run(() => Save(userId, raceId, 2))));

        var state = UserState(userId);
        Assert.Equal(rewards.Sum(reward => reward.XpGained), state.Xp);
        Assert.Equal(rewards.Sum(reward => reward.BananasGained), state.Bananas);
        Assert.Equal(1, rewards.Count(reward => reward.FirstRace));
        Assert.Equal(6, state.Results);
    }

    [Fact]
    public async Task Mistakes_AddUp_StreakResetsOnError_BadEntriesSkipped()
    {
        var (userId, _) = Seed(0);
        static MistakesDto Dto(params (string Word, bool HadError)[] words) => new MistakesDto
        {
            Keys = new List<MistakesDto.KeyCount>
            {
                new() { Expected = "ž", Typed = "z", Count = 2 },
                new() { Expected = "ž", Typed = "ž", Count = 3 },
                new() { Expected = "ab", Typed = "a", Count = 9 } // ne vienas simbolis – praleidžiama
            },
            Words = words.Select(w => new MistakesDto.WordTry { Word = w.Word, HadError = w.HadError }).ToList()
        };

        async Task Send(MistakesDto dto)
        {
            using var db = NewContext();
            await new RaceService(db).SaveMistakesAsync(userId, dto);
        }

        await Send(Dto(("Žąsis,", true), ("namas", false)));
        await Send(Dto(("žąsis", false), ("žąsis.", false)));
        using (var db = NewContext())
        {
            var keys = db.UserKeyStats.Where(k => k.UserId == userId).ToList();
            Assert.Equal(4, keys.Single(k => k.Expected == "ž" && k.Typed == "z").Count);
            Assert.Equal(6, keys.Single(k => k.Expected == "ž" && k.Typed == "ž").Count);
            Assert.Equal(2, keys.Count);
            // be klaidų parašytas žodis eilutės nesukuria; skyryba ir didžiosios raidės nesvarbios
            var word = db.UserWordMistakes.Single(w => w.UserId == userId);
            Assert.Equal(("žąsis", 1, 2), (word.Word, word.Misses, word.CorrectStreak));
        }

        await Send(Dto(("ŽĄSIS", true)));
        using (var db = NewContext())
        {
            var word = db.UserWordMistakes.Single(w => w.UserId == userId);
            Assert.Equal((2, 0), (word.Misses, word.CorrectStreak));
        }
    }

    [Fact]
    public async Task MistakeStats_MergesCase_HidesRareLetters_SkipsMasteredWords()
    {
        var (userId, _) = Seed(0);
        using (var db = NewContext())
        {
            db.UserKeyStats.AddRange(
                new UserKeyStat { UserId = userId, Expected = "š", Typed = "š", Count = 15 },
                new UserKeyStat { UserId = userId, Expected = "Š", Typed = "š", Count = 3 }, // pamirštas Shift
                new UserKeyStat { UserId = userId, Expected = "š", Typed = "s", Count = 2 },
                new UserKeyStat { UserId = userId, Expected = "q", Typed = "w", Count = 5 }); // tik 5 bandymai
            db.UserWordMistakes.AddRange(
                new UserWordMistake { UserId = userId, Word = "šuo", Misses = 2, CorrectStreak = 0 },
                new UserWordMistake { UserId = userId, Word = "kelias", Misses = 9, CorrectStreak = RaceService.MasteredStreak });
            db.SaveChanges();
        }

        using var ctx = NewContext();
        var stats = await new RaceService(ctx).GetMistakeStatsAsync(userId);

        Assert.Equal(25, stats.Attempts);
        Assert.Equal(60, stats.Accuracy, 3);
        var letter = Assert.Single(stats.WeakLetters);
        Assert.Equal(("š", 20, 5), (letter.Letter, letter.Attempts, letter.Misses));
        Assert.Equal(("q", "w"), (stats.Confusions[0].Expected, stats.Confusions[0].Typed));
        Assert.Equal("šuo", Assert.Single(stats.Words).Word);
        Assert.Equal(1, stats.MasteredWords);
    }

    [Fact]
    public async Task TrainingText_UnlearnedWordsTwice_PlusWeakLetterWords_NullWithoutData()
    {
        var (userId, _) = Seed(0);
        using (var db = NewContext())
            Assert.Null(await new RaceService(db).BuildTrainingTextAsync(userId));

        using (var db = NewContext())
        {
            db.UserWordMistakes.AddRange(
                new UserWordMistake { UserId = userId, Word = "šuo", Misses = 2 },
                new UserWordMistake { UserId = userId, Word = "kelias", Misses = 9, CorrectStreak = RaceService.MasteredStreak });
            db.UserKeyStats.AddRange(
                new UserKeyStat { UserId = userId, Expected = "ž", Typed = "ž", Count = 15 },
                new UserKeyStat { UserId = userId, Expected = "ž", Typed = "z", Count = 5 });
            db.SaveChanges();
        }

        using var ctx = NewContext();
        var words = (await new RaceService(ctx).BuildTrainingTextAsync(userId)).Split(' ');

        Assert.Equal(2, words.Count(word => word == "šuo"));
        Assert.DoesNotContain("kelias", words);
        // 30 žodžių: 2 neišmokti + 28 su silpniausia raide
        Assert.Equal(30, words.Length);
        Assert.All(words.Where(word => word != "šuo"), word => Assert.Contains("ž", word));
    }

    [Fact]
    public async Task Shop_BuysOnce_EvenInParallel_RejectsWithoutBananas_ReequipIsFree()
    {
        var (userId, _) = Seed(0);
        using (var db = NewContext())
        {
            db.Users.Single(u => u.Id == userId).Bananas = 400;
            db.SaveChanges();
        }
        async Task<string> Equip(string itemId)
        {
            using var db = NewContext();
            return await new RaceService(db).EquipItemAsync(userId, itemId);
        }

        // du paspaudimai vienu metu – nuskaičiuojama tik kartą
        var results = await Task.WhenAll(Task.Run(() => Equip("hat-banana")), Task.Run(() => Equip("hat-banana")));
        Assert.All(results, Assert.Null);
        Assert.Equal("Trūksta 50 bananų.", await Equip("hat-red"));
        Assert.Null(await Equip("hat-green")); // nemokama
        Assert.Null(await Equip("hat-banana")); // jau nupirkta – nemokamai
        Assert.Equal("Tokios prekės nėra.", await Equip("hat-gold"));

        using var ctx = NewContext();
        var user = ctx.Users.Single(u => u.Id == userId);
        Assert.Equal((250, "hat-banana"), (user.Bananas, user.HatId));
        Assert.Equal("hat-banana", ctx.UserItems.Single(i => i.UserId == userId).ItemId);
        var card = await new RaceService(ctx).GetPlayerCardAsync(userId);
        Assert.Equal(("#FFC93C", "#8B5E3C", "classic"), (card.HatColor, card.FurColor, card.Track));
    }

    [Fact]
    public async Task Shop_BackgroundAndFrame_BuyEquipAndAppearOnCard()
    {
        var (userId, _) = Seed(0);
        using (var db = NewContext())
        {
            db.Users.Single(u => u.Id == userId).Bananas = 300;
            db.SaveChanges();
        }
        async Task<string> Equip(string itemId)
        {
            using var db = NewContext();
            return await new RaceService(db).EquipItemAsync(userId, itemId);
        }

        Assert.Null(await Equip("bg-mint"));
        Assert.Equal("Trūksta 500 bananų.", await Equip("frame-leaf"));
        using var ctx = NewContext();
        Assert.Equal(100, ctx.Users.Single(u => u.Id == userId).Bananas);
        var card = await new RaceService(ctx).GetPlayerCardAsync(userId);
        Assert.Equal(("rgba(127, 191, 147, 0.55)", "none"), (card.BackgroundColor, card.FrameColor));
        Assert.Null(await Equip("bg-none")); // numatytasis – nemokamai
    }

    [Fact]
    public void Catalog_EveryCategoryHasOneDefault_AndNonShopItemsExplainHowToGetThem()
    {
        foreach (var group in ShopCatalog.Items.GroupBy(item => item.Category))
            Assert.Single(group, item => item.IsDefault);
        Assert.All(ShopCatalog.Items.Where(item => !item.IsPurchasable && !item.IsDefault),
            item => Assert.False(string.IsNullOrWhiteSpace(item.Unlock)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public async Task LevelPrizes_OldPlayerIsPaidOnceAndGetsItemsForReachedLevels()
    {
        var (userId, raceIds) = Seed(2);
        using (var db = NewContext())
        {
            // pradžia 15 lygio: senoje kreivėje tai 10 lygis (jau gauta), naujoje – 15 (viena lenktynė 16-o nepasieks)
            var user = db.Users.Single(u => u.Id == userId);
            user.Xp = Enumerable.Range(0, 20000).First(xp => Progression.LevelFor(xp) == 15) + 5;
            user.ClaimedLevel = 10;
            user.Bananas = 0;
            db.SaveChanges();
        }
        var level = 15;
        var expected = Enumerable.Range(11, level - 10).Sum(Progression.LevelUpBananas);

        var first = await Save(userId, raceIds[0], 4);
        using (var db = NewContext())
        {
            var user = db.Users.Single(u => u.Id == userId);
            Assert.Equal(level, user.ClaimedLevel);
            Assert.Equal(expected + first.BananasGained, user.Bananas);
            var items = db.UserItems.Where(i => i.UserId == userId).Select(i => i.ItemId).ToHashSet();
            Assert.Equal(
                ShopCatalog.Items.Where(i => i.Source == ItemSource.Level && i.Level <= level).Select(i => i.Id).ToHashSet(),
                items);
            Assert.Contains("bg-sand", items);
            Assert.Contains("hat-student", items);
            Assert.Contains("fur-latte", items);
        }
        Assert.Equal(3, first.NewItems.Count);

        // antra lenktynė: skola nebemokama, prizai nesikartoja
        var bananasBefore = UserState(userId).Bananas;
        var second = await Save(userId, raceIds[1], 4);
        Assert.Empty(second.NewItems);
        Assert.Equal(bananasBefore + second.BananasGained, UserState(userId).Bananas);
    }

    [Fact]
    public async Task LevelPrizes_CrossingLevelFiveInRace_GrantsSandBackground_AndCanBeEquipped()
    {
        var (userId, raceIds) = Seed(1);
        using (var db = NewContext())
        {
            db.Users.Single(u => u.Id == userId).Xp = 690; // iki 5 lygio (700) trūksta 10 XP
            db.SaveChanges();
        }
        var reward = await Save(userId, raceIds[0], 1);

        Assert.True(reward.LeveledUp);
        Assert.Equal("bg-sand", Assert.Single(reward.NewItems).Id);
        using var ctx = NewContext();
        Assert.Equal(5, ctx.Users.Single(u => u.Id == userId).ClaimedLevel);
        Assert.Null(await new RaceService(ctx).EquipItemAsync(userId, "bg-sand"));
        // dar nepasiektas lygis – neperkamas
        Assert.Contains("nusipirkti negalima", await new RaceService(ctx).EquipItemAsync(userId, "hat-king"));
    }


    private async Task<LessonSaveResult> SaveLesson(int userId, string lessonId, int stars)
    {
        using var db = NewContext();
        return await new RaceService(db).SaveLessonResultAsync(userId, lessonId, stars, 99, 40);
    }

    [Fact]
    public async Task LessonPath_ModuleBonusAndMilestonePrizes_AreGivenOnlyOnce()
    {
        var (userId, _) = Seed(0);
        var first = LessonCatalog.Modules[0];

        // visi pirmo modulio pamokos su 3 žvaigždutėmis: baigtas modulis + visos žvaigždutės
        LessonSaveResult last = null;
        foreach (var lesson in first.Lessons)
            last = await SaveLesson(userId, lesson.Id, 3);
        Assert.Equal(2, last.Reward.Notes.Count);
        using (var db = NewContext())
        {
            var claims = db.UserClaims.Where(c => c.UserId == userId && !c.Key.StartsWith(Achievements.ClaimPrefix))
                .Select(c => c.Key).ToHashSet();
            Assert.Equal(new[] { "module:1", "mastery:1" }.ToHashSet(), claims);
        }

        // pakartojimas nieko nebeduoda
        var again = await SaveLesson(userId, first.Lessons[0].Id, 3);
        Assert.Empty(again.Reward.Notes);

        // 10-a įveikta pamoka atrakina neoninį foną
        var next = LessonCatalog.All.Skip(first.Lessons.Count).Take(LessonPath.Milestones[0].Lessons - first.Lessons.Count).ToList();
        foreach (var lesson in next)
            last = await SaveLesson(userId, lesson.Id, 1);
        Assert.Contains(last.Reward.NewItems, i => i.Id == "bg-neon");
        using (var db = NewContext())
            Assert.Contains(db.UserItems, i => i.UserId == userId && i.ItemId == "bg-neon");
        Assert.Contains(last.Reward.Notes, n => n.StartsWith("10 pamokų"));
        Assert.Null(await EquipAs(userId, "bg-neon"));
    }

    private async Task<string> EquipAs(int userId, string itemId)
    {
        using var db = NewContext();
        return await new RaceService(db).EquipItemAsync(userId, itemId);
    }

    [Fact]
    public void LessonPath_MilestonesMatchCatalogAndShopItems()
    {
        Assert.Equal(LessonCatalog.All.Count, LessonPath.Milestones[^1].Lessons);
        foreach (var milestone in LessonPath.Milestones)
        {
            var item = ShopCatalog.Find(milestone.ItemId);
            Assert.Equal(ItemSource.Lesson, item.Source);
            Assert.Equal(milestone.Lessons, item.Lessons);
        }
    }


    [Fact]
    public async Task Achievements_FirstRaceAndPerfectAccuracy_GiveXpOnlyOnce()
    {
        var (userId, raceIds) = Seed(2, claimAchievements: false);

        var first = await Save(userId, raceIds[0], 4); // 60 WPM, 100 % tikslumas
        var bonus = Achievements.Find("first-race").Xp + Achievements.Find("perfect-race").Xp + Achievements.Find("wpm-50").Xp;
        Assert.Equal(3, first.Notes.Count);
        Assert.Contains(first.Notes, n => n.Contains("Pirma lenktynė") && n.Contains("+25 XP"));
        Assert.Equal(UserState(userId).Xp, first.Xp);
        Assert.True(first.XpGained >= bonus);

        var xpAfterFirst = UserState(userId).Xp;
        var second = await Save(userId, raceIds[1], 4);
        Assert.Empty(second.Notes);
        Assert.Equal(xpAfterFirst + second.XpGained, UserState(userId).Xp);
    }

    [Fact]
    public async Task Achievements_OpeningProfile_GivesOldPlayersTheirEarnedOnes()
    {
        var (userId, raceIds) = Seed(1, claimAchievements: false);
        using (var db = NewContext())
        {
            db.RaceResults.Add(new RaceResult { UserId = userId, RaceId = raceIds[0], WPM = 85, Accuracy = 99, Place = 1 });
            db.SaveChanges();
        }
        using (var db = NewContext())
            await new RaceService(db).SettleLevelsForUserAsync(userId);
        using (var db = NewContext())
        {
            var keys = db.UserClaims.Where(c => c.UserId == userId).Select(c => c.Key).ToHashSet();
            foreach (var key in new[] { "first-race", "wpm-50", "wpm-80", "first-win" })
                Assert.Contains(Achievements.ClaimPrefix + key, keys);
            Assert.DoesNotContain(Achievements.ClaimPrefix + "wpm-100", keys);
            Assert.Equal(25 + 75 + 200 + 75, db.Users.Single(u => u.Id == userId).Xp);
        }
    }

    [Fact]
    public void Achievements_KeysAreUnique_AndXpUsesPlanTiers()
    {
        Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Key).Distinct().Count());
        Assert.All(Achievements.All, a => Assert.Contains(a.Xp, new[] { 25, 75, 200, 500 }));
    }

}

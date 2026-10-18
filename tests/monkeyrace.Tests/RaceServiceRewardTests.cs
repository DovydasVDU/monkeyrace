using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Tikra SQLite DB faile, kad būtų patikrintos ir migracijos, ir lygiagretus įrašymas
public sealed class RaceServiceRewardTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-test-{Guid.NewGuid():N}.sqlite");

    private AppDbContext NewContext() => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}").Options);

    private (int UserId, int[] RaceIds) Seed(int races)
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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}

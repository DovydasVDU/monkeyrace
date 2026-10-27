using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Pamokų katalogas, žodžių parinkimas ir adaptyvi logika (be DB ir HTTP)
public class LessonEngineTests
{
    private static Lesson Lesson(string id) => LessonCatalog.Find(id)!;

    private static readonly IReadOnlyDictionary<string, KeyPerf> NoKeys = new Dictionary<string, KeyPerf>();

    private static string Core(string item) => item.Split('-')[0].TrimEnd(',', '.', '?').ToLowerInvariant();

    [Fact]
    public void Catalog_HasUniqueIds_AndCumulativeKeys()
    {
        Assert.Equal(LessonCatalog.All.Count, LessonCatalog.All.Select(lesson => lesson.Id).Distinct().Count());
        for (var i = 1; i < LessonCatalog.All.Count; i++)
        {
            var previous = LessonCatalog.All[i - 1];
            var current = LessonCatalog.All[i];
            Assert.Equal(i, current.Index);
            // kiekviena pamoka leidžia visus ankstesnių raides: žodžiai sudaryti tik iš išmoktų klavišų
            Assert.StartsWith(previous.AllowedKeys, current.AllowedKeys);
        }
        Assert.True(LessonCatalog.IsFree(Lesson("1-3")));
        Assert.False(LessonCatalog.IsFree(Lesson("1-4")));
        Assert.Null(LessonCatalog.Next(LessonCatalog.All[^1]));
    }

    [Fact]
    public void WordBank_IsLarge()
    {
        Assert.True(LessonWords.Lower.Count > 400);
        Assert.Contains("kasa", LessonWords.Lower);
        Assert.All(LessonWords.Lower, word => Assert.True(word.All(char.IsLower)));
    }

    [Fact]
    public void Items_UseOnlyAllowedKeys_AndContainNewLetters()
    {
        var rng = new Random(1);
        foreach (var lesson in LessonCatalog.All.Where(l => l.Kind != LessonKind.Sentences))
        {
            for (var level = 0; level <= LessonEngine.MaxLevel; level++)
            {
                var items = LessonEngine.WordItems(lesson, level, 10, Array.Empty<char>(), Array.Empty<string>(), rng);
                Assert.NotEmpty(items);
                var letters = LessonEngine.FocusKeys(lesson).Where(char.IsLetter).ToArray();
                foreach (var item in items)
                {
                    foreach (var c in item)
                        Assert.True(lesson.AllowedKeys.Contains(char.ToLowerInvariant(c)) || c == '-',
                            $"{lesson.Id}: '{c}' ({item}) nėra leidžiamas");
                    if (letters.Length > 0 && !lesson.Capitals)
                        Assert.True(item.Any(c => letters.Contains(c)), $"{lesson.Id}: {item} neturi naujos raidės");
                }
            }
        }
    }

    [Fact]
    public void Drills_AreOnlyInFirstThreeLessons_RestAreRealWords()
    {
        var rng = new Random(2);
        var real = new HashSet<string>(LessonWords.Lower.Concat(LessonWords.Names.Select(n => n.ToLowerInvariant())));
        // 1-5 (G ir H): lietuviškų žodžių tik raidėms a d f g h j k l s yra vos trys, todėl dalis elementų – tariamos raidžių grupės
        foreach (var lesson in LessonCatalog.All.Where(l => l.Kind == LessonKind.Words && l.Id != "1-5"))
        {
            var items = Enumerable.Range(0, 5)
                .SelectMany(_ => LessonEngine.WordItems(lesson, lesson.StartLevel, 10, Array.Empty<char>(), Array.Empty<string>(), rng))
                .ToList();
            var share = items.Count(item => real.Contains(Core(item))) / (double)items.Count;
            Assert.True(share >= 0.7, $"{lesson.Id}: tikrų žodžių tik {share:P0}");
        }
        foreach (var lesson in LessonCatalog.All.Where(l => l.Kind == LessonKind.Drill))
        {
            foreach (var item in LessonEngine.WordItems(lesson, 1, 20, Array.Empty<char>(), Array.Empty<string>(), rng))
                Assert.DoesNotContain("fff", item);
        }
    }

    [Fact]
    public void CapitalsLesson_UsesCapitals_WithoutLithuanianCapitalInitials()
    {
        var rng = new Random(3);
        var lesson = Lesson("5-1");
        var items = Enumerable.Range(0, 20)
            .SelectMany(_ => LessonEngine.WordItems(lesson, 1, 10, Array.Empty<char>(), Array.Empty<string>(), rng)).ToList();
        Assert.Contains(items, item => char.IsUpper(item[0]));
        Assert.All(items, item => Assert.True(!char.IsUpper(item[0]) || (item[0] is >= 'A' and <= 'Z')));
    }

    [Fact]
    public void LtCapitalsLesson_MostlyLithuanianCapitalInitials()
    {
        var rng = new Random(8);
        var lesson = Lesson("5-2");
        Assert.True(lesson.LtCapitals);
        var items = Enumerable.Range(0, 10)
            .SelectMany(_ => LessonEngine.WordItems(lesson, 1, 10, Array.Empty<char>(), Array.Empty<string>(), rng)).ToList();
        var lithuanian = items.Count(item => "ĄČĘĖĮŠŲŪŽ".Contains(item[0]));
        Assert.True(lithuanian >= items.Count / 2, $"lietuviška didžioji tik {lithuanian}/{items.Count}");
    }

    [Fact]
    public void WeakKeys_AreRepeated_InReviewLessons()
    {
        var rng = new Random(4);
        var lesson = Lesson("1-6");
        var keys = new Dictionary<string, KeyPerf> { ["g"] = new() { Attempts = 10, Misses = 8 }, ["a"] = new() { Attempts = 20, Misses = 0 } };
        var weak = LessonEngine.WeakKeys(lesson, keys, LessonEngine.Pace("iprastai"));
        Assert.Equal(new[] { 'g' }, weak);
        var items = LessonEngine.WordItems(lesson, 1, 30, weak, Array.Empty<string>(), rng);
        Assert.True(items.Count(item => item.Contains('g')) >= 5);
    }

    [Fact]
    public void RecentWords_AreAvoided_WhenPoolIsLargeEnough()
    {
        var rng = new Random(5);
        var lesson = Lesson("2-1");
        var first = LessonEngine.WordItems(lesson, 1, 10, Array.Empty<char>(), Array.Empty<string>(), rng);
        var recent = first.Select(Core).ToList();
        var second = LessonEngine.WordItems(lesson, 1, 10, Array.Empty<char>(), recent, rng);
        Assert.DoesNotContain(second, item => recent.Contains(Core(item)));
    }

    [Theory]
    [InlineData(100, 1, 2)]
    [InlineData(97, 1, 2)]
    [InlineData(96, 2, 3)]
    [InlineData(93, 2, 2)]
    [InlineData(87, 2, 1)]
    [InlineData(50, 0, 0)]
    [InlineData(99, 3, 3)]
    public void AdaptLevel_RaisesAndLowers(double accuracy, int level, int expected)
    {
        Assert.Equal(expected, LessonEngine.AdaptLevel(level, accuracy, round: 1));
        Assert.Equal(level, LessonEngine.AdaptLevel(level, accuracy, round: 0));
    }

    [Fact]
    public void FinishesEarly_WhenKeysMastered_AndAccuracyHigh()
    {
        var lesson = Lesson("1-4");
        var pace = LessonEngine.Pace("iprastai");
        var mastered = new Dictionary<string, KeyPerf> { ["a"] = new() { Attempts = pace.MinAttempts, Misses = 0 } };
        var learning = new Dictionary<string, KeyPerf> { ["a"] = new() { Attempts = 3, Misses = 0 } };

        Assert.False(LessonEngine.IsDone(lesson, pace, 0, 100, 20, mastered).Done);
        Assert.Equal((true, "early"), LessonEngine.IsDone(lesson, pace, 1, 98, 20, mastered));
        Assert.False(LessonEngine.IsDone(lesson, pace, 1, 98, 20, learning).Done);
        Assert.False(LessonEngine.IsDone(lesson, pace, 1, 90, 20, mastered).Done);
        Assert.Equal((true, "max"), LessonEngine.IsDone(lesson, pace, LessonEngine.MaxRounds, 50, 5, learning));
    }

    [Fact]
    public void TargetWpmLesson_NeedsSpeed()
    {
        var lesson = Lesson("6-4");
        var pace = LessonEngine.Pace("iprastai");
        Assert.False(LessonEngine.IsDone(lesson, pace, 2, 99, 20, NoKeys).Done);
        Assert.True(LessonEngine.IsDone(lesson, pace, 2, 98, 31, NoKeys).Done);
    }

    [Fact]
    public void Stars_FollowPaceAndTargetWpm()
    {
        var normal = LessonEngine.Pace("iprastai");
        Assert.Equal(0, LessonEngine.StarsFor(Lesson("2-1"), normal, 91.9, 30));
        Assert.Equal(1, LessonEngine.StarsFor(Lesson("2-1"), normal, 92, 30));
        Assert.Equal(2, LessonEngine.StarsFor(Lesson("2-1"), normal, 95, 30));
        Assert.Equal(3, LessonEngine.StarsFor(Lesson("2-1"), normal, 98, 30));
        Assert.Equal(2, LessonEngine.StarsFor(Lesson("6-4"), normal, 99, 25));
        Assert.Equal(3, LessonEngine.StarsFor(Lesson("6-4"), normal, 99, 30));
    }

    [Fact]
    public void NextRound_ReturnsItems_OrDone()
    {
        var lesson = Lesson("2-2");
        var pace = LessonEngine.Pace("iprastai");
        var plan = LessonEngine.NextRound(lesson, pace, 0, lesson.StartLevel, 100, 0, NoKeys,
            Array.Empty<string>(), Array.Empty<string>(), new Random(6));
        Assert.False(plan.Done);
        Assert.Equal(pace.RoundItems, plan.Items.Count);
        Assert.Equal("ou", plan.Focus);

        var done = LessonEngine.NextRound(lesson, pace, LessonEngine.MaxRounds, 2, 80, 10, NoKeys,
            Array.Empty<string>(), Array.Empty<string>(), new Random(6));
        Assert.True(done.Done);
    }

    [Fact]
    public void ExtractSentences_KeepsOnlyTypeableOnes()
    {
        var lesson = Lesson("5-4");
        var texts = new[]
        {
            "Saulė kyla kiekvieną rytą. Šuo loja garsiai ir ilgai. „Labas“, tarė jis ir nuėjo. Jis turi 5 obuolius namuose. Namuose šiandien labai ramu."
        };
        var sentences = LessonEngine.ExtractSentences(lesson, texts);
        Assert.Contains("Saulė kyla kiekvieną rytą.", sentences);
        Assert.DoesNotContain(sentences, s => s.Contains('5') || s.Contains('„'));
        // lietuviškų didžiųjų jau išmokta (5-2), todėl Š sakinio pradžioje leidžiama
        Assert.Contains("Šuo loja garsiai ir ilgai.", sentences);
        // ... o iki tos pamokos – ne, nes didžiųjų lietuviškų raidžių rinkimas priklauso nuo išdėstymo
        Assert.DoesNotContain(LessonEngine.ExtractSentences(Lesson("5-1"), texts), s => s.StartsWith("Šuo"));
        Assert.Contains("Namuose šiandien labai ramu.", sentences);
    }

    [Fact]
    public void SentenceLessons_FallBackToWordLines()
    {
        var lesson = Lesson("5-4");
        var items = LessonEngine.SentenceItems(lesson, 1, 3, Array.Empty<string>(), Array.Empty<string>(), new Random(7));
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.EndsWith(".", item));
    }

    [Fact]
    public void LessonReward_OnlyForNewStars()
    {
        var first = Progression.LessonReward(1, 0, 0);
        Assert.Equal((20, 3), (first.Xp, first.Bananas));
        var all = Progression.LessonReward(3, 0, 0);
        Assert.Equal((40, 8), (all.Xp, all.Bananas));
        var improve = Progression.LessonReward(3, 1, 0);
        Assert.Equal((20, 5), (improve.Xp, improve.Bananas));
        var repeat = Progression.LessonReward(2, 3, 0);
        Assert.Equal((0, 0), (repeat.Xp, repeat.Bananas));
        // pakilus lygiui: +20 x lygis bananų, kaip lenktynėse
        var levelUp = Progression.LessonReward(1, 0, 95);
        Assert.Equal(2, levelUp.LevelAfter);
        Assert.Equal(3 + 40, levelUp.Bananas);
    }
}

// Pamokos rezultatas per tikrą HTTP (sesija, cookie, anti-cheat) ir SQLite DB faile
public sealed class LessonCompleteTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"monkeyrace-lessons-{Guid.NewGuid():N}.sqlite");
    private readonly WebApplicationFactory<Program> _factory;

    public LessonCompleteTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path}" })));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }

    private static object Rounds(int chars, int errors, int elapsedMs) =>
        new { pace = "iprastai", rounds = new[] { new { chars, errors, elapsedMs } } };

    [Fact]
    public async Task Complete_SavesProgress_RewardsOnce_AndRejectsImpossibleSpeed()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var register = await client.PostAsync("/Auth/Register", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = "mokinys", ["password"] = "slaptas1" }));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // be puslapio atidarymo sesijoje nėra pamokos
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/pamokos/1-1/complete", Rounds(40, 1, 3000))).StatusCode);

        (await client.GetAsync("/pamokos/1-1")).EnsureSuccessStatusCode();
        // 5000 simbolių per sekundę neįmanoma
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/pamokos/1-1/complete", Rounds(1000, 0, 1000))).StatusCode);

        (await client.GetAsync("/pamokos/1-1")).EnsureSuccessStatusCode();
        await Task.Delay(1300); // rašymo laikas negali būti ilgesnis už laiką nuo puslapio atidarymo
        var response = await client.PostAsJsonAsync("/pamokos/1-1/complete", Rounds(40, 1, 3000));
        response.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.Single(u => u.Username == "mokinys");
            var progress = db.UserLessonProgresses.Single(p => p.UserId == user.Id);
            Assert.Equal("1-1", progress.LessonId);
            Assert.Equal(2, progress.Stars); // 40 / 41 = 97,6 %
            // 1 žvaigždutė 20 XP + 3 bananai, 2 žvaigždutė 10 XP + 2 bananai
            Assert.Equal(30, user.Xp);
            Assert.Equal(5, user.Bananas);
        }

        // kartojimas to paties rezultato nieko neprideda
        (await client.GetAsync("/pamokos/1-1")).EnsureSuccessStatusCode();
        await Task.Delay(1300);
        (await client.PostAsJsonAsync("/pamokos/1-1/complete", Rounds(40, 1, 3000))).EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(30, db.Users.Single(u => u.Username == "mokinys").Xp);
            Assert.Equal(2, db.UserLessonProgresses.Single().Attempts);
        }
    }

    [Fact]
    public async Task Guest_CanPlayFirstLessons_ButNotLaterOnes()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await client.GetAsync("/pamokos")).EnsureSuccessStatusCode();
        (await client.GetAsync("/pamokos/1-3")).EnsureSuccessStatusCode();
        var later = await client.PostAsJsonAsync("/pamokos/1-4/round", new { pace = "iprastai", round = 0 });
        Assert.Equal(HttpStatusCode.Unauthorized, later.StatusCode);

        var first = await client.PostAsJsonAsync("/pamokos/1-1/round", new { pace = "iprastai", round = 0 });
        first.EnsureSuccessStatusCode();
        Assert.Contains("\"items\"", await first.Content.ReadAsStringAsync());
    }

    public void Dispose()
    {
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}

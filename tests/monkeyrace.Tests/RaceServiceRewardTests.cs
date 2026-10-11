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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }
}

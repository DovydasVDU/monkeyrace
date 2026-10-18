using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using monkeyrace.Controllers;

// /api/stats/text/{id}: tik to teksto klasikinės online lenktynės, geriausi pirmi
public sealed class TextLeaderboardTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"monkeyrace-textlb-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public async Task OnlyOnlineClassicResultsOfThisText_BestFirst()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_db}").Options);
        db.Database.Migrate();
        var (textId, otherTextId) = (db.TextSnippets.OrderBy(t => t.Id).First().Id, db.TextSnippets.OrderBy(t => t.Id).Skip(1).First().Id);
        var ana = new User { Username = "ana", PasswordHash = "x" };
        var jonas = new User { Username = "jonas", PasswordHash = "x" };
        Race race(int? text, bool online, int? seconds = null, string replay = null) =>
            new() { TextSnippetId = text, IsMultiplayer = online, DurationSeconds = seconds, StartedAt = DateTime.UtcNow, ReplayJson = replay };
        db.RaceResults.AddRange(
            new RaceResult { User = ana, Race = race(textId, true, replay: "{}"), WPM = 50, Accuracy = 97.5 },
            new RaceResult { User = jonas, Race = race(textId, true), WPM = 70, Accuracy = 99 },
            new RaceResult { User = jonas, Race = race(textId, false), WPM = 120, Accuracy = 100 },      // solo
            new RaceResult { User = jonas, Race = race(null, true, 30), WPM = 110, Accuracy = 100 },    // žodžių režimas
            new RaceResult { User = ana, Race = race(otherTextId, true), WPM = 90, Accuracy = 100 });   // kitas tekstas
        db.SaveChanges();

        var ok = Assert.IsType<OkObjectResult>(await new StatsApiController(db, new RaceService(db)).GetTextLeaderboard(textId));
        var json = JsonSerializer.SerializeToElement(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var rows = json.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(new[] { "jonas", "ana" }, rows.Select(r => r.GetProperty("username").GetString()));
        Assert.Equal(new[] { false, true }, rows.Select(r => r.GetProperty("hasReplay").GetBoolean()));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }
}

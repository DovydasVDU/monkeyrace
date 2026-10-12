using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public sealed class TextImporterTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"monkeyrace-texts-{Guid.NewGuid():N}.sqlite");
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"monkeyrace-texts-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public void Import_FillsMetadata_SecondRunAddsNothing_SelectionSkipsOldSeeds()
    {
        File.WriteAllLines(_file, new[]
        {
            """{"id": "a1", "body": "Tie dideli miškai buvo pono.", "author": "Jonas Biliūnas", "work": "Lazda", "sourceUrl": "https://lt.wikisource.org/wiki/Lazda", "license": "PD-PATIKRINTI", "flags": ["quotes", "ellipsis"]}""",
            """{"id": "b2", "body": "Kaunas yra miestas.", "author": "Vikipedija", "work": "Kaunas", "sourceUrl": "https://lt.wikipedia.org/wiki/Kaunas", "license": "CC BY-SA 4.0", "flags": []}"""
        });
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_db}").Options);
        db.Database.Migrate();

        Assert.Equal(2, TextImporter.Import(db, _file));
        Assert.Equal(0, TextImporter.Import(db, _file));

        var lazda = db.TextSnippets.Single(t => t.SourceId == "a1");
        Assert.Equal("Jonas Biliūnas", lazda.Author);
        Assert.Equal(5, lazda.WordCount);
        Assert.Equal(new[] { "Kabutės", "Daugtaškis" }, lazda.FlagLabels);
        Assert.Equal("Viešasis domenas", lazda.LicenseLabel);

        // seni seed tekstai lieka DB, bet atsitiktinai renkami tik importuoti
        for (var i = 0; i < 20; i++)
            Assert.NotNull(new RaceService(db).GetRandomSnippetAsync(new List<int>()).Result.SourceId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); File.Delete(_file); } catch (IOException) { }
    }
}

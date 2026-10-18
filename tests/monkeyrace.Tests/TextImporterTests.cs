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

    [Theory]
    [InlineData("- Palauk! - sustabdė jį dėdė . - Kaipgi", "- Palauk! - sustabdė jį dėdė. - Kaipgi")]
    [InlineData("intakų ( Kaukysa , Murle ), o", "intakų (Kaukysa, Murle), o")]
    [InlineData("kaip gumbas;pasidaro, stovi jau :šalia", "kaip gumbas; pasidaro, stovi jau: šalia")]
    [InlineData("tai ... ne, 3,5 km.", "tai ... ne, 3,5 km.")]
    public void Clean_FixesScraperSpacing(string raw, string expected) =>
        Assert.Equal(expected, TextImporter.Clean(raw));

    [Fact]
    public void Import_UpdatesChangedBodyOfExistingText()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_db}").Options);
        db.Database.Migrate();
        File.WriteAllText(_file, """{"id": "c3", "body": "Vilnius yra sostinė .", "author": "Vikipedija", "work": "Vilnius", "sourceUrl": "https://lt.wikipedia.org/wiki/Vilnius", "license": "CC BY-SA 4.0", "flags": []}""");
        Assert.Equal(1, TextImporter.Import(db, _file));
        Assert.Equal("Vilnius yra sostinė.", db.TextSnippets.Single(t => t.SourceId == "c3").Content);

        File.WriteAllText(_file, """{"id": "c3", "body": "Vilnius yra Lietuvos sostinė.", "author": "Vikipedija", "work": "Vilnius", "sourceUrl": "https://lt.wikipedia.org/wiki/Vilnius", "license": "CC BY-SA 4.0", "flags": []}""");
        Assert.Equal(0, TextImporter.Import(db, _file));
        var text = db.TextSnippets.AsNoTracking().Single(t => t.SourceId == "c3");
        Assert.Equal("Vilnius yra Lietuvos sostinė.", text.Content);
        Assert.Equal(4, text.WordCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_db); File.Delete(_file); } catch (IOException) { }
    }
}

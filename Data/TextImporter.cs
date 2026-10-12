// Data/TextImporter.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// Įkelia Data/texts-lt.jsonl (generuoja tekstai-i-duomenis.py) į TextSnippets.
// Kviečiamas paleidžiant programą (be failo programa nepasileidžia – nebūtų ką rašyti); jau esantys tekstai (pagal SourceId) praleidžiami, todėl kartoti saugu.
public static class TextImporter
{
    private sealed record Row(string Id, string Body, string Author, string Work,
        string SourceUrl, string License, string[] Flags);

    public static int Import(AppDbContext db, string path)
    {
        var existing = db.TextSnippets.Where(t => t.SourceId != null)
            .Select(t => t.SourceId).ToHashSet();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var added = File.ReadLines(path)
            .Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<Row>(line, options))
            .Where(row => existing.Add(row.Id))
            .Select(row => new TextSnippet
            {
                SourceId = row.Id,
                Content = row.Body,
                WordCount = row.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                Author = row.Author,
                Work = row.Work,
                SourceUrl = row.SourceUrl,
                License = row.License,
                Flags = string.Join(",", row.Flags ?? Array.Empty<string>())
            })
            .ToList();

        db.TextSnippets.AddRange(added);
        db.SaveChanges();
        return added.Count;
    }
}

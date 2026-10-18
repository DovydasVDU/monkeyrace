// Data/TextImporter.cs
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

// Įkelia Data/texts-lt.jsonl (generuoja tekstai-i-duomenis.py) į TextSnippets.
// Kviečiamas paleidžiant programą (be failo programa nepasileidžia – nebūtų ką rašyti).
// Jau esantis tekstas (pagal SourceId) neįkeliamas iš naujo, bet jo turinys atnaujinamas,
// jei faile pataisytas – taip rankiniai ir Clean pataisymai pasiekia ir veikiančią DB.
public static class TextImporter
{
    private sealed record Row(string Id, string Body, string Author, string Work,
        string SourceUrl, string License, string[] Flags);

    public static int Import(AppDbContext db, string path)
    {
        var existing = db.TextSnippets.Where(t => t.SourceId != null)
            .ToDictionary(t => t.SourceId);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var added = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            var row = JsonSerializer.Deserialize<Row>(line, options);
            var body = Clean(row.Body);
            var wordCount = body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (existing.TryGetValue(row.Id, out var snippet))
            {
                if (snippet.Content != body)
                {
                    snippet.Content = body;
                    snippet.WordCount = wordCount;
                }
                continue;
            }

            existing[row.Id] = new TextSnippet
            {
                SourceId = row.Id,
                Content = body,
                WordCount = wordCount,
                Author = row.Author,
                Work = row.Work,
                SourceUrl = row.SourceUrl,
                License = row.License,
                Flags = string.Join(",", row.Flags ?? Array.Empty<string>())
            };
            db.TextSnippets.Add(existing[row.Id]);
            added++;
        }

        db.SaveChanges();
        return added;
    }

    // Scraperio palikti tarpai: „dėdė . -“, „( Kaukysa , Murle )“, „gumbas;pasidaro“.
    // Daugtaškis su tarpu prieš („tai ... ne“) paliekamas.
    public static string Clean(string text)
    {
        text = Regex.Replace(text, @"\s+([,;:!?]|\.(?!\.))", "$1");
        text = Regex.Replace(text, @"([,;:])(?=\p{L})", "$1 ");
        text = Regex.Replace(text, @"\(\s+", "(");
        text = Regex.Replace(text, @"\s+\)", ")");
        return Regex.Replace(text, @"\s{2,}", " ").Trim();
    }
}

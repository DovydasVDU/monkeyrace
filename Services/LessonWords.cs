// Services/LessonWords.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Žodžiai pamokoms: ranka sudarytas Data/lesson-words.txt (+ dažniausi žodžiai iš words-lt.txt, kad pasirinkimas būtų platesnis).
// Mažosios raidės – paprasti žodžiai, didžiąja pradedami – vardai (naudojami tik atrakinus Shift).
public static class LessonWords
{
    public static readonly IReadOnlyList<string> Lower;
    public static readonly IReadOnlyList<string> Names;

    static LessonWords()
    {
        var lower = new HashSet<string>();
        var names = new HashSet<string>();
        foreach (var file in new[] { "lesson-words.txt", "words-lt.txt" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", file);
            if (!File.Exists(path))
                continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var word = raw.Trim();
                // komentarai, frazės ir žodžiai su skyryba į pamokas nepatenka
                if (word.Length == 0 || word.StartsWith('#') || !word.All(char.IsLetter))
                    continue;
                if (char.IsUpper(word[0]))
                    names.Add(word);
                else if (word.All(char.IsLower))
                    lower.Add(word);
            }
        }
        Lower = lower.OrderBy(w => w, StringComparer.Ordinal).ToArray();
        Names = names.OrderBy(w => w, StringComparer.Ordinal).ToArray();
    }
}

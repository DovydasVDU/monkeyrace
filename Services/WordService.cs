// Services/WordService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Žodžių režimo tekstas: atsitiktiniai žodžiai iš 1000 dažniausių lietuviškų.
public static class WordService
{
    // Failas nuskaitomas vieną kartą (pirmą kartą panaudojus klasę) ir laikomas atmintyje
    private static readonly string[] Words = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, "Data", "words-lt.txt"))
        .Select(word => word.Trim())
        .Where(word => word.Length > 0)
        .Distinct()
        .ToArray();

    public static readonly int[] AllowedSeconds = { 15, 30, 60, 120 };

    // Treniruokliui: atsitiktiniai žodžiai, kuriuose yra bent viena iš nurodytų raidžių
    public static string[] WithLetters(IReadOnlyCollection<string> letters, int count)
    {
        var pool = Words.Where(word => letters.Any(letter =>
            word.Contains(letter, StringComparison.OrdinalIgnoreCase))).ToArray();
        return pool.Length == 0
            ? Array.Empty<string>()
            : Enumerable.Range(0, count).Select(_ => pool[Random.Shared.Next(pool.Length)]).ToArray();
    }

    // Žodžių daugiau, nei leistų parašyti RaceService.MaxWpm per visą laiką, todėl tekstas žaidėjui "nesibaigia"
    public static string Generate(int seconds)
    {
        var count = (int)Math.Ceiling(RaceService.MaxWpm * seconds / 60d * 1.2);
        return string.Join(" ", Enumerable.Range(0, count)
            .Select(_ => Words[Random.Shared.Next(Words.Length)]));
    }
}

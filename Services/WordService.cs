// Services/WordService.cs
using System;
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

    // Žodžių daugiau, nei leistų parašyti RaceService.MaxWpm per visą laiką, todėl tekstas žaidėjui "nesibaigia"
    public static string Generate(int seconds)
    {
        var count = (int)Math.Ceiling(RaceService.MaxWpm * seconds / 60d * 1.2);
        return string.Join(" ", Enumerable.Range(0, count)
            .Select(_ => Words[Random.Shared.Next(Words.Length)]));
    }
}

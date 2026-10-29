// Services/MonthlyChallenge.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Mėnesio iššūkis: kas greičiau ir tiksliau parašo kelis labai ilgus lietuviškus žodžius. Taisyklės vienoje vietoje.
// Rezultatas = laikas (matuoja serveris) + bausmė už kiekvieną klaidą; skaičiuojamas geriausias bandymas.
public static class MonthlyChallenge
{
    // Pirmas žodis visada tas pats („mėnesio žodis“), kiti trys keičiasi kiekvieną mėnesį
    public const string SignatureWord = "nebeprisikiškiakopūstėliaujantiesiems";

    // shortcut: žodžius reikėtų peržiūrėti dėl rašybos (kaip ir Data/lesson-words.txt)
    private static readonly string[] Pool =
    {
        "nepakartojamumas", "nesuinteresuotumas", "neįsivaizduojamas", "nepasiduodantiesiems",
        "tarptautiškumas", "visuomeniškumas", "nesusipratimas", "pasididžiavimas",
        "nesąmoningumas", "pasikartojančiuosius"
    };

    public const int ExtraWords = 3;
    // tiek sekundžių rodoma atgalinė atskaita prieš pradedant; serveris ją atima iš išmatuoto laiko
    public const int CountdownSeconds = 3;
    public const int ErrorPenaltySeconds = 2;
    // vienkartinis prizas už pirmą dalyvavimą kiekvieną mėnesį
    public const int ParticipationXp = 50;
    public const int ParticipationBananas = 20;

    public static string MonthKey(DateTime utcNow) => utcNow.ToString("yyyy-MM");

    // Tie patys žodžiai visiems, kiek kartų bepaprašytum tą patį mėnesį
    public static IReadOnlyList<string> WordsFor(string month)
    {
        var extra = Pool.OrderBy(word => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(month + word))))
            .Take(ExtraWords);
        return new[] { SignatureWord }.Concat(extra).ToList();
    }

    public static int TotalChars(string month) => WordsFor(month).Sum(word => word.Length);

    public static double Score(double seconds, int errors) => Math.Round(seconds + errors * ErrorPenaltySeconds, 2);

    // Per greitas rezultatas laikomas sukčiavimu (kaip RaceService.MaxWpm lenktynėse)
    public static bool IsPlausible(string month, double seconds) =>
        seconds > 0 && TotalChars(month) / 5.0 / (seconds / 60.0) <= RaceService.MaxWpm;
}

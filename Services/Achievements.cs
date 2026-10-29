// Services/Achievements.cs
using System;
using System.Collections.Generic;
using System.Linq;

// Skaičiai, pagal kuriuos tikrinami pasiekimai (RaceService.LoadAchievementStatsAsync surenka iš DB)
public sealed class AchievementStats
{
    public int Races { get; init; }
    public int BestWpm { get; init; }
    public bool PerfectRace { get; init; }
    public bool LastFiveAccurate { get; init; }
    public int OnlineWins { get; init; }
    public int LessonsDone { get; init; }
    public int ThreeStarLessons { get; init; }
    public int ModulesDone { get; init; }
    public int Level { get; init; }
    public bool BoughtSomething { get; init; }
    public int OwnedItems { get; init; }
    public int CategoriesOwned { get; init; }
}

public sealed record Achievement(string Key, string Group, string Title, string Description, int Xp,
    Func<AchievementStats, bool> IsDone);

// Pasiekimai su XP (25 / 75 / 200 / 500). Tik tie, kuriems pakanka jau saugomų duomenų; serijoms, „naktiniam paukščiui“
// ir kitiems reikėtų naujo sekimo. Prizas vienkartinis: UserClaims raktas "ach:{Key}".
public static class Achievements
{
    public const string ClaimPrefix = "ach:";

    public static readonly IReadOnlyList<Achievement> All = new List<Achievement>
    {
        new("first-race", "Pirmieji žingsniai", "Pirma lenktynė", "Baik savo pirmas lenktynes.", 25, s => s.Races >= 1),
        new("first-lesson", "Pirmieji žingsniai", "Pirma pamoka", "Įveik pirmą aklojo rašymo pamoką.", 25, s => s.LessonsDone >= 1),
        new("first-purchase", "Pirmieji žingsniai", "Pirmas pirkinys", "Nusipirk ką nors parduotuvėje.", 25, s => s.BoughtSomething),
        new("perfect-race", "Tikslumas", "Be klaidų", "Baik lenktynes su 100 % tikslumu.", 75, s => s.PerfectRace),
        new("wpm-50", "Greitis", "50 WPM", "Pasiek 50 WPM lenktynėse.", 75, s => s.BestWpm >= 50),
        new("three-stars", "Pamokos", "Trys žvaigždutės", "Gauk tris žvaigždutes vienoje pamokoje.", 75, s => s.ThreeStarLessons >= 1),
        new("first-win", "Online", "Pirma pergalė", "Laimėk online lenktynes.", 75, s => s.OnlineWins >= 1),
        new("module-1", "Pamokos", "Pirmas modulis", "Įveik visas vieno modulio pamokas.", 75, s => s.ModulesDone >= 1),
        new("wpm-80", "Greitis", "80 WPM", "Pasiek 80 WPM lenktynėse.", 200, s => s.BestWpm >= 80),
        new("wins-10", "Online", "10 pergalių", "Laimėk 10 online lenktynių.", 200, s => s.OnlineWins >= 10),
        new("accurate-5", "Tikslumas", "Penkios tikslios", "Penkios paskutinės lenktynės iš eilės su ≥ 98 % tikslumu.", 200, s => s.LastFiveAccurate),
        new("races-100", "Veikla", "100 lenktynių", "Baik 100 lenktynių.", 200, s => s.Races >= 100),
        new("items-5", "Kolekcija", "Penki daiktai", "Turėk 5 skirtingus daiktus.", 200, s => s.OwnedItems >= 5),
        new("modules-3", "Pamokos", "Trys moduliai", "Įveik visas trijų modulių pamokas.", 200, s => s.ModulesDone >= 3),
        new("wpm-100", "Greitis", "100 WPM", "Pasiek 100 WPM lenktynėse.", 500, s => s.BestWpm >= 100),
        new("races-1000", "Veikla", "1 000 lenktynių", "Baik 1 000 lenktynių.", 500, s => s.Races >= 1000),
        new("all-stars", "Pamokos", "Visos pamokos su trimis", "Gauk tris žvaigždutes visose pamokose.", 500, s => s.ThreeStarLessons >= LessonCatalog.All.Count),
        new("level-100", "Lygis", "100 lygis", "Pasiek aukščiausią lygį.", 500, s => s.Level >= Progression.MaxLevel),
        new("collector", "Kolekcija", "Kolekcininkas", "Turėk bent po vieną gautą ar nupirktą daiktą iš kiekvienos kategorijos.", 500, s => s.CategoriesOwned >= 5),
    };

    public static Achievement Find(string key) => All.FirstOrDefault(a => a.Key == key);
}

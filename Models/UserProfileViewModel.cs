using System;
using System.Collections.Generic;

// tai yra objektas, kurį suformuoja UserController. Paima duomenis iš db, sudeda į UserProfileViewModel ir siunčia į view. 
public class UserProfileViewModel
{
    public string Username { get; set; } = string.Empty;
    public int AverageWpm { get; set; }
    public double AverageAccuracy { get; set; }
    // visos vartotojo lenktynės (PastRaces – tik pirmas puslapis), kad žinotume, ar rodyti „Užkrauti daugiau“
    public int TotalRaces { get; set; }
    public List<PastRaceViewModel> PastRaces { get; set; } = new List<PastRaceViewModel>();
    public MistakeStats Mistakes { get; set; } = new MistakeStats();
    // Parduotuvė: nupirktų ir užsidėtų prekių Id (ShopCatalog)
    public HashSet<string> OwnedItems { get; set; } = new HashSet<string>();
    // Įvykdytų pasiekimų raktai (Achievements.All)
    public HashSet<string> UnlockedAchievements { get; set; } = new HashSet<string>();
    public HashSet<string> EquippedItems { get; set; } = new HashSet<string>();
}
// lygiai taspats, tik surenkama buvusių naudotojo žaidimų istorija
public class PastRaceViewModel
{
    public int ResultId { get; set; }
    // /race/replay/{ResultId}; tik online lenktynėms po perklausos atsiradimo
    public bool HasReplay { get; set; }
    public DateTime CompletedAt { get; set; }
    public int Wpm { get; set; }
    public double Accuracy { get; set; }
    public int ErrorCount { get; set; }
    public string Difficulty { get; set; } = string.Empty;
}

// Profilio „Klaidų analizė“ (RaceService.GetMistakeStatsAsync)
public class MistakeStats
{
    // visi paspaudimai, kai buvo laukiama konkretaus simbolio
    public int Attempts { get; set; }
    public double Accuracy { get; set; }
    public List<LetterStat> WeakLetters { get; set; } = new List<LetterStat>();
    public List<LetterConfusion> Confusions { get; set; } = new List<LetterConfusion>();
    // dar neišmokti žodžiai (CorrectStreak < RaceService.MasteredStreak)
    public List<UserWordMistake> Words { get; set; } = new List<UserWordMistake>();
    public int MasteredWords { get; set; }
}

public class LetterStat
{
    public string Letter { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int Misses { get; set; }
    public double MissPercent => Attempts == 0 ? 0 : Misses * 100d / Attempts;
}

public class LetterConfusion
{
    public string Expected { get; set; } = string.Empty;
    public string Typed { get; set; } = string.Empty;
    public int Count { get; set; }
}

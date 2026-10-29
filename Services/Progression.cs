using System;

public enum RaceMode
{
    Solo,
    Multiplayer
}

public sealed class Reward
{
    public int Xp { get; init; }
    public int Bananas { get; init; }
    public int LevelBefore { get; init; }
    public int LevelAfter { get; init; }
}

// Visos XP, lygių ir bananų taisyklės vienoje vietoje. Grynos funkcijos, todėl lengvai testuojamos.
public static class Progression
{
    // Kiek XP reikia pasiekti lygį (indeksas 0 = 1 lygis). Maksimalus 100 lygis (~189 000 XP).
    private static readonly int[] LevelThresholds = BuildThresholds();

    // Pirmi 10 lygių nekeisti (žaidėjų pasiekta pažanga); toliau kiekvienas lygis kainuoja 30 XP daugiau nei
    // ankstesnis (10 -> 11 lygis 750 XP, 99 -> 100 – 3 400 XP), kaina apvalinama iki 50.
    private static int[] BuildThresholds()
    {
        var thresholds = new int[100];
        int[] first = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };
        first.CopyTo(thresholds, 0);
        for (var level = 11; level <= 100; level++)
            thresholds[level - 1] = thresholds[level - 2] + (int)Math.Round((700 + (level - 10) * 30) / 50.0) * 50;
        return thresholds;
    }

    public static int MaxLevel => LevelThresholds.Length;

    // Titulas keičiasi kas 10 lygių: 1–10 Naujokas, 11–20 Pirmokas, ... 91–100 Džiunglių Karalius
    private static readonly string[] Titles =
    {
        "Naujokas", "Pirmokas", "Antrokas", "Trečiokas", "Beždžionėlė",
        "Gorila", "Tigras", "Ilgapirštis", "Pirštų Meistras", "Džiunglių Karalius"
    };

    public static string TitleFor(int level) => Titles[Math.Clamp((level - 1) / 10, 0, Titles.Length - 1)];

    // Bananai už naują lygį: iki 10 – 20 × lygis (kaip buvo), toliau 60, o kiekvieno dešimtuko pirmas lygis +300
    public static int LevelUpBananas(int level) =>
        level <= 10 ? 20 * level : 60 + (level % 10 == 1 ? 300 : 0);

    // WPM virš šios ribos laikomas nepatikimu ir apkerpamas
    private const double MaxRewardedWpm = 200;

    public static int LevelFor(int xp)
    {
        var level = 1;
        while (level < MaxLevel && xp >= LevelThresholds[level])
            level++;
        return level;
    }

    // Kiek XP trūksta iki kito lygio; null maksimaliame lygyje
    public static int? ToNextLevel(int xp)
    {
        var level = LevelFor(xp);
        return level == MaxLevel ? (int?)null : LevelThresholds[level] - xp;
    }

    // Kiek dabartinio lygio juostos užpildyta (0..1), XP juostai
    public static double LevelProgress(int xp)
    {
        var level = LevelFor(xp);
        if (level == MaxLevel)
            return 1;
        var floor = LevelThresholds[level - 1];
        return (double)(xp - floor) / (LevelThresholds[level] - floor);
    }

    // accuracy procentais (0..100), kaip saugoma RaceResults; place null solo režime;
    // durationSeconds – žodžių režimo trukmė (null klasikiniam tekstui)
    public static Reward RewardFor(
        RaceMode mode, int? place, double wpm, double accuracy,
        bool finished, bool isFirstRace, int xpBefore, int? durationSeconds = null)
    {
        var levelBefore = LevelFor(xpBefore);
        if (!finished)
            return new Reward { LevelBefore = levelBefore, LevelAfter = levelBefore };

        wpm = Math.Clamp(wpm, 0, MaxRewardedWpm);
        var accuracyFactor = Math.Clamp(accuracy, 0, 100) / 100;

        double xp;
        int bananas;
        if (mode == RaceMode.Solo)
        {
            xp = 10 + wpm / 4;
            bananas = 1;
        }
        else
        {
            xp = 20 + wpm / 2 + PlaceBonusXp(place);
            bananas = 2 + PlaceBonusBananas(place);
        }

        xp *= accuracyFactor;
        // trumpas žodžių režimas neturi duoti tiek XP, kiek minutė rašymo
        if (durationSeconds != null)
            xp *= Math.Min(1, durationSeconds.Value / 60d);
        if (accuracy >= 95)
            bananas += 1;
        if (isFirstRace)
        {
            xp += 50;
            bananas += 10;
        }

        var xpGained = (int)Math.Round(xp);
        var levelAfter = LevelFor(xpBefore + xpGained);
        // bananai už kiekvieną pakeltą lygį (pvz. 1 -> 3: 20*2 + 20*3)
        for (var level = levelBefore + 1; level <= levelAfter; level++)
            bananas += LevelUpBananas(level);

        return new Reward
        {
            Xp = xpGained,
            Bananas = bananas,
            LevelBefore = levelBefore,
            LevelAfter = levelAfter
        };
    }

    // Pamoka: apdovanojama tik už NAUJAI užsidirbtas žvaigždutes (kartojant jau gautą rezultatą nieko nepridedama).
    // 1 žvaigždutė – 20 XP ir 3 bananai, 2 – 10 XP ir 2, 3 – 10 XP ir 3, pakilus lygiui – kaip lenktynėse.
    public static Reward LessonReward(int stars, int previousStars, int xpBefore)
    {
        var levelBefore = LevelFor(xpBefore);
        var xp = 0;
        var bananas = 0;
        for (var star = Math.Max(0, previousStars) + 1; star <= Math.Min(3, stars); star++)
        {
            xp += star == 1 ? 20 : 10;
            bananas += star switch { 1 => 3, 2 => 2, _ => 3 };
        }

        var levelAfter = LevelFor(xpBefore + xp);
        for (var level = levelBefore + 1; level <= levelAfter; level++)
            bananas += LevelUpBananas(level);

        return new Reward { Xp = xp, Bananas = bananas, LevelBefore = levelBefore, LevelAfter = levelAfter };
    }

    private static int PlaceBonusXp(int? place) => place switch
    {
        1 => 30,
        2 => 20,
        3 => 10,
        _ => 0
    };

    private static int PlaceBonusBananas(int? place) => place switch
    {
        1 => 8,
        2 => 4,
        3 => 2,
        _ => 0
    };
}

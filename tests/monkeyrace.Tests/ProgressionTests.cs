public class ProgressionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(3199, 9)]
    [InlineData(3200, 10)]
    [InlineData(99999, 10)]
    public void LevelFor_Thresholds(int xp, int level)
    {
        Assert.Equal(level, Progression.LevelFor(xp));
    }

    [Fact]
    public void ToNextLevel_IsNullAtMaxLevel()
    {
        Assert.Equal(1, Progression.ToNextLevel(99));
        Assert.Equal(150, Progression.ToNextLevel(100));
        Assert.Null(Progression.ToNextLevel(3200));
    }

    private static Reward Multi(int? place, double wpm = 60, double accuracy = 100,
        bool finished = true, bool firstRace = false, int xpBefore = 0) =>
        Progression.RewardFor(RaceMode.Multiplayer, place, wpm, accuracy, finished, firstRace, xpBefore);

    [Fact]
    public void FirstPlace_GetsMoreThanFourth()
    {
        var first = Multi(1);
        var fourth = Multi(4);
        Assert.True(first.Xp > fourth.Xp);
        Assert.True(first.Bananas > fourth.Bananas);
        Assert.Equal(20 + 30 + 30, first.Xp);    // 20 + 60/2 + 30
        Assert.Equal(2 + 8 + 1, first.Bananas);  // bazė + vieta + tikslumas >= 95
    }

    [Fact]
    public void Dnf_GetsNothing()
    {
        var reward = Multi(1, finished: false, firstRace: true, xpBefore: 99);
        Assert.Equal(0, reward.Xp);
        Assert.Equal(0, reward.Bananas);
        Assert.Equal(reward.LevelBefore, reward.LevelAfter);
    }

    [Fact]
    public void Xp_ScaledByAccuracy_AndBananaBonusOnlyFrom95()
    {
        var reward = Progression.RewardFor(RaceMode.Solo, null, 40, 90, true, false, 0);
        Assert.Equal(18, reward.Xp);       // (10 + 40/4) * 0.9
        Assert.Equal(1, reward.Bananas);   // be +1, nes 90 % < 95 %

        var accurate = Progression.RewardFor(RaceMode.Solo, null, 40, 95, true, false, 0);
        Assert.Equal(2, accurate.Bananas);
    }

    [Fact]
    public void WordsMode_XpScaledByDuration_MinuteAndLongerIsFull()
    {
        // (10 + 40/4) = 20 XP už minutę ar daugiau, 15 s – ketvirtis
        Assert.Equal(5, Progression.RewardFor(RaceMode.Solo, null, 40, 100, true, false, 0, 15).Xp);
        Assert.Equal(20, Progression.RewardFor(RaceMode.Solo, null, 40, 100, true, false, 0, 60).Xp);
        Assert.Equal(20, Progression.RewardFor(RaceMode.Solo, null, 40, 100, true, false, 0, 120).Xp);
        // pirmųjų lenktynių premija nemažinama
        Assert.Equal(55, Progression.RewardFor(RaceMode.Solo, null, 40, 100, true, true, 0, 15).Xp);
    }

    [Fact]
    public void FirstRaceBonus_OnlyWhenFirst()
    {
        // wpm 20: 30 XP be bonuso, 80 XP su juo, lygio riba (100) nekertama
        var first = Multi(4, wpm: 20, firstRace: true);
        var later = Multi(4, wpm: 20, firstRace: false);
        Assert.Equal(later.Xp + 50, first.Xp);
        Assert.Equal(later.Bananas + 10, first.Bananas);
    }

    [Fact]
    public void LevelBonus_ForEachLevelGained()
    {
        // 90 XP (1 lygis) + (20 + 120/2 + 30) + 50 = 250 XP -> 3 lygis
        var reward = Multi(1, wpm: 120, firstRace: true, xpBefore: 90);
        Assert.Equal(1, reward.LevelBefore);
        Assert.Equal(3, reward.LevelAfter);
        Assert.Equal(2 + 8 + 1 + 10 + 20 * 2 + 20 * 3, reward.Bananas);
    }

    [Fact]
    public void Wpm_IsClampedTo200()
    {
        Assert.Equal(Multi(4, wpm: 200).Xp, Multi(4, wpm: 9999).Xp);
        Assert.Equal(Multi(4, wpm: 0).Xp, Multi(4, wpm: -50).Xp);
    }
}

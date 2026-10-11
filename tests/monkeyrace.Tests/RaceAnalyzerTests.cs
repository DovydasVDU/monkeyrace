public class RaceAnalyzerTests
{
    private static RaceRoom Room(params RacePlayer[] players) => new RaceRoom
    {
        RaceId = "test",
        Text = new string('a', 20),
        Players = players.ToList()
    };

    private static void Tick(RaceRoom room, int atMs, params (string Key, int Chars)[] values)
    {
        room.Timeline.Add(new RaceTimelineTick
        {
            AtMs = atMs,
            Values = values.ToDictionary(value => value.Key, value => value.Chars)
        });
    }

    [Fact]
    public void FinalTick_SkipsDnfPlayer_InsteadOfThrowing()
    {
        // Anksčiau DNF žaidėjas mesdavo KeyNotFoundException ir lenktynės niekada nesibaigdavo
        var room = Room(
            new RacePlayer { PlayerKey = "a", IsFinished = true },
            new RacePlayer { PlayerKey = "b", IsDnf = true, CorrectChars = 7 });

        RaceAnalyzer.AddTimelineTick(room, 1000);

        var tick = Assert.Single(room.Timeline);
        Assert.Equal(20, tick.Values["a"]);
        Assert.False(tick.Values.ContainsKey("b"));
    }

    [Fact]
    public void Analysis_CountsOvertakeOnlyWhenPasserWasBehind()
    {
        var room = Room(new RacePlayer { PlayerKey = "a" }, new RacePlayer { PlayerKey = "b" });
        Tick(room, 250, ("a", 4), ("b", 0));   // a pirmauja nuo starto: tai ne aplenkimas
        Tick(room, 500, ("a", 6), ("b", 0));
        Tick(room, 750, ("a", 7), ("b", 9));   // b pralenkia a
        Tick(room, 1000, ("a", 8), ("b", 12));
        Tick(room, 1250, ("a", 9), ("b", 15));

        var analysis = RaceAnalyzer.CreateAnalysis(room);

        var overtake = Assert.Single(analysis.Overtakes);
        Assert.Equal("b", overtake.PasserId);
        Assert.Equal("a", overtake.PassedId);
        Assert.Equal(1250, overtake.AtMs);
        Assert.Equal(1, analysis.LeaderChanges);
    }
}

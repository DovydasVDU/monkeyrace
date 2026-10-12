using System;
using System.Collections.Generic;
using System.Linq;

// Grynos funkcijos lenktynių laiko juostai ir analizei (lyderiai, aplenkimai).
// Nepriklauso nuo SignalR ir laikmačių, todėl jas galima testuoti atskirai.
public static class RaceAnalyzer
{
    public static object CreateTimeline(RaceRoom room)
    {
        return new
        {
            tickMs = 250,
            players = room.Players.Select((player, index) => new
            {
                id = player.PlayerKey,
                name = player.Username,
                colorIndex = index,
                series = room.Timeline
                    .TakeWhile(tick => tick.Values.ContainsKey(player.PlayerKey))
                    .Select(tick => tick.Values[player.PlayerKey])
                    .ToList()
            }).ToList()
        };
    }

    public static void AddTimelineTick(RaceRoom room, int elapsedMs)
    {
        if (room.Timeline.Count > 0 &&
            room.Timeline[room.Timeline.Count - 1].AtMs >= elapsedMs)
            return;

        var tick = new RaceTimelineTick
        {
            AtMs = elapsedMs,
            Values = room.Players.Where(player => !player.IsDnf).ToDictionary(
                player => player.PlayerKey,
                player => player.IsFinished && room.DurationSeconds == 0
                    ? room.Text.Length : player.CorrectChars)
        };
        room.Timeline.Add(tick);
        foreach (var player in room.Players.Where(player => !player.IsDnf))
        {
            var value = tick.Values[player.PlayerKey];
            if (!player.FirstReachedAtMs.ContainsKey(value))
                player.FirstReachedAtMs[value] = elapsedMs;
        }
    }

    public static RaceAnalysis CreateAnalysis(RaceRoom room)
    {
        var players = room.Players.ToDictionary(player => player.PlayerKey);
        var leaders = new List<RaceLeaderSegment>();
        var overtakes = new List<RaceOvertake>();
        string previousLeader = null;
        var leaderStartedAt = 0;
        var pairLeadTicks = new Dictionary<string, int>();
        var pairReported = new HashSet<string>();
        var pairWasBehind = new HashSet<string>();

        foreach (var tick in room.Timeline)
        {
            var leader = tick.Values
                .OrderByDescending(item => item.Value)
                .ThenBy(item => players[item.Key].FirstReachedAtMs.TryGetValue(
                    item.Value, out var reached) ? reached : int.MaxValue)
                .FirstOrDefault().Key;
            if (string.IsNullOrEmpty(leader))
                continue;

            if (leader != previousLeader)
            {
                if (previousLeader != null)
                    leaders.Add(new RaceLeaderSegment
                    {
                        PlayerId = previousLeader,
                        FromMs = leaderStartedAt,
                        ToMs = tick.AtMs
                    });
                previousLeader = leader;
                leaderStartedAt = tick.AtMs;
            }

            foreach (var passer in tick.Values)
            {
                foreach (var passed in tick.Values)
                {
                    if (passer.Key == passed.Key)
                        continue;

                    var pair = passer.Key + "|" + passed.Key;
                    // Aplenkimas tik tada, jei prieš tai passer tikrai buvo už passed
                    if (passer.Value < passed.Value)
                        pairWasBehind.Add(pair);
                    if (passer.Value - passed.Value < 3 || !pairWasBehind.Contains(pair))
                        continue;

                    pairLeadTicks[pair] = pairLeadTicks.TryGetValue(pair, out var count)
                        ? count + 1
                        : 1;
                    if (pairLeadTicks[pair] >= 2 && pairReported.Add(pair))
                    {
                        overtakes.Add(new RaceOvertake
                        {
                            AtMs = tick.AtMs,
                            PasserId = passer.Key,
                            PassedId = passed.Key
                        });
                    }
                }
            }
        }

        if (previousLeader != null && room.Timeline.Count > 0)
            leaders.Add(new RaceLeaderSegment
            {
                PlayerId = previousLeader,
                FromMs = leaderStartedAt,
                ToMs = room.Timeline.Last().AtMs
            });

        var totalMs = room.Timeline.Count == 0 ? 0 : room.Timeline.Last().AtMs;
        return new RaceAnalysis
        {
            LeaderSegments = leaders,
            Overtakes = overtakes,
            LeaderChanges = Math.Max(0, leaders.Count - 1),
            LeaderTimeMs = room.Players.ToDictionary(
                player => player.PlayerKey,
                player => leaders.Where(segment => segment.PlayerId == player.PlayerKey)
                    .Sum(segment => segment.ToMs - segment.FromMs)),
            TotalMs = totalMs
        };
    }
}

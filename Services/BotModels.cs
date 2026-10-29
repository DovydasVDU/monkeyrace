using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public sealed class BotOptions
{
    public bool Enabled { get; set; } = true;
    public int FillToPlayers { get; set; } = 3;
    public double DefaultWpm { get; set; } = 40;
    public double[] SkillFactors { get; set; } = new[] { .85, 1d, 1.1 };
    public double WpmMin { get; set; } = 20;
    public double WpmMax { get; set; } = 120;
    public double[] AccuracyRange { get; set; } = new[] { .92, .98 };
}

// Boto charakteris kaip chess.com: Adaptive – pagal žaidėjo istoriją, kiti – fiksuotas greitis
public enum BotLevel
{
    Adaptive,
    Easy,
    Medium,
    Hard
}

public sealed class BotSkillProvider
{
    public static (double Wpm, double AccuracyMin, double AccuracyMax) Preset(BotLevel level) => level switch
    {
        BotLevel.Easy => (25, .88, .93),
        BotLevel.Medium => (45, .92, .96),
        _ => (75, .96, .99)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BotOptions> _options;
    private readonly object _sync = new object();
    // svečio greitis pamirštamas po GuestWpmLifetime, kad žodynas neaugtų iki perkrovimo
    private static readonly TimeSpan GuestWpmLifetime = TimeSpan.FromDays(7);
    private readonly Dictionary<string, (double Wpm, DateTime At)> _guestWpm =
        new Dictionary<string, (double Wpm, DateTime At)>();

    public BotSkillProvider(
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<double> GetTargetWpmAsync(
        IEnumerable<RacePlayer> humans, int botIndex)
    {
        var options = _options.Value;
        var baseline = options.DefaultWpm;
        var humanList = humans.Where(player => !player.IsBot).ToList();
        var userIds = humanList.Where(player => player.UserId.HasValue)
            .Select(player => player.UserId.Value).ToList();
        if (userIds.Count > 0)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Iš DB imame tik po 10 paskutinių kiekvieno žaidėjo rezultatų (kambaryje max 4 žmonės)
            var recent = new List<int>();
            foreach (var userId in userIds)
                recent.AddRange(await db.RaceResults
                    .Where(result => result.UserId == userId)
                    .OrderByDescending(result => result.CompletedAt)
                    .Take(10)
                    .Select(result => result.WPM)
                    .ToListAsync());
            if (recent.Count > 0)
                baseline = recent.Average();
        }
        else
        {
            var remembered = new List<double>();
            lock (_sync)
            {
                foreach (var player in humanList)
                    if (_guestWpm.TryGetValue(player.PlayerKey, out var value))
                        remembered.Add(value.Wpm);
            }
            if (remembered.Count > 0)
                baseline = remembered.Average();
        }

        var factors = options.SkillFactors?.Length > 0
            ? options.SkillFactors
            : new[] { 1d };
        var factor = factors[Math.Abs(botIndex) % factors.Length];
        var variation = 0.95 + ((botIndex * 37) % 11) / 100d;
        var target = baseline * factor * variation;
        return Math.Max(options.WpmMin, Math.Min(options.WpmMax, target));
    }

    public void RememberGuestWpm(string playerKey, double wpm)
    {
        if (string.IsNullOrWhiteSpace(playerKey) || !playerKey.StartsWith("guest:"))
            return;
        var now = DateTime.UtcNow;
        lock (_sync)
        {
            foreach (var old in _guestWpm.Where(pair => now - pair.Value.At > GuestWpmLifetime).ToList())
                _guestWpm.Remove(old.Key);
            _guestWpm[playerKey] = (wpm, now);
        }
    }
}

public sealed class BotPlan
{
    private readonly int[] _progressAtTicks;
    public int FinishMs { get; }
    public double Accuracy { get; }
    public double TargetWpm { get; }
    public int ErrorCount { get; }

    private BotPlan(
        int[] progressAtTicks, int finishMs, double targetWpm,
        double accuracy, int errorCount)
    {
        _progressAtTicks = progressAtTicks;
        FinishMs = finishMs;
        Accuracy = accuracy;
        TargetWpm = targetWpm;
        ErrorCount = errorCount;
    }

    public int CorrectCharsAt(int elapsedMs)
    {
        if (elapsedMs <= 0)
            return 0;
        var index = Math.Min(_progressAtTicks.Length - 1, elapsedMs / 250);
        return _progressAtTicks[index];
    }

    public static BotPlan Create(
        string text,
        double targetWpm,
        double accuracy,
        int seed)
    {
        var random = new Random(seed);
        var charsPerMs = targetWpm * 5d / 60000d;
        var finishMs = Math.Max(250, (int)Math.Ceiling(text.Length / charsPerMs));
        var ticks = new int[Math.Max(1, (finishMs / 250) + 1)];
        var progress = 0d;
        for (var tick = 0; tick < ticks.Length; tick++)
        {
            var jitter = .88 + random.NextDouble() * .24;
            progress += charsPerMs * 250 * jitter;
            ticks[tick] = Math.Min(text.Length, (int)Math.Floor(progress));
        }
        ticks[ticks.Length - 1] = text.Length;
        var errorCount = accuracy >= 1
            ? 0
            : Math.Max(0, (int)Math.Round(text.Length * (1 - accuracy) / accuracy));
        return new BotPlan(ticks, finishMs, targetWpm, accuracy, errorCount);
    }
}

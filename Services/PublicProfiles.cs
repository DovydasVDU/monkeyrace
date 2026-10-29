// Services/PublicProfiles.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

// Viešas žaidėjo profilis: tik tai, ką galima rodyti kitiems (be bananų, tikslaus XP, lenktynių skaičiaus, el. pašto)
public sealed class PublicProfile
{
    public PublicPlayerCard Card { get; init; }
    public int LessonsDone { get; init; }
    public int LessonsTotal { get; init; }
    public List<PublicAchievement> Achievements { get; init; } = new();
    public int AchievementsTotal { get; init; }
    public List<PublicItem> Items { get; init; } = new();
}

public sealed record PublicAchievement(string Key, string Title, int Xp);
public sealed record PublicItem(string Id, string Name, string Category, string Rarity);
public sealed record PlayerSearchHit(string Username, int Level, string Title);

public static class PublicProfiles
{
    public const int MinSearchLength = 3;
    public const int MaxSearchResults = 8;

    public static bool CanView(ProfileVisibility visibility, bool viewerLoggedIn) => visibility switch
    {
        ProfileVisibility.Public => true,
        ProfileVisibility.Members => viewerLoggedIn,
        _ => false
    };

    // null – profilio nėra ARBA jis tau uždarytas (toks pat atsakymas, kad nebūtų galima patikrinti, ar vardas užimtas)
    public static async Task<PublicProfile> GetAsync(AppDbContext db, RaceService raceService, string username, int? viewerId)
    {
        var user = await db.Users.Where(u => u.Username == username)
            .Select(u => new { u.Id, u.ProfileVisibility }).FirstOrDefaultAsync();
        if (user == null || (viewerId != user.Id && !CanView(user.ProfileVisibility, viewerId != null)))
            return null;

        var card = await raceService.GetPlayerCardAsync(user.Id);
        var lessons = await raceService.GetLessonSummaryAsync(user.Id);
        var unlocked = (await db.UserClaims.Where(c => c.UserId == user.Id && c.Key.StartsWith(Achievements.ClaimPrefix))
            .Select(c => c.Key).ToListAsync()).Select(k => k.Substring(Achievements.ClaimPrefix.Length)).ToHashSet();
        var items = (await db.UserItems.Where(i => i.UserId == user.Id).Select(i => i.ItemId).ToListAsync())
            .Select(ShopCatalog.Find).Where(i => i != null).ToList();

        static int RarityOrder(string rarity) => rarity switch { "legendary" => 0, "epic" => 1, "rare" => 2, _ => 3 };
        return new PublicProfile
        {
            Card = card.ForOthers(),
            LessonsDone = lessons.Done,
            LessonsTotal = lessons.Total,
            Achievements = Achievements.All.Where(a => unlocked.Contains(a.Key))
                .Select(a => new PublicAchievement(a.Key, a.Title, a.Xp)).ToList(),
            AchievementsTotal = Achievements.All.Count,
            Items = items.OrderBy(i => RarityOrder(i.Rarity)).ThenBy(i => i.Name)
                .Select(i => new PublicItem(i.Id, i.Name, i.Category, i.Rarity)).ToList()
        };
    }

    // Paieška pagal vardo pradžią, tik žaidėjai, kuriuos šis žiūrovas gali matyti
    public static async Task<List<PlayerSearchHit>> SearchAsync(AppDbContext db, string query, bool viewerLoggedIn)
    {
        query = (query ?? "").Trim();
        if (query.Length < MinSearchLength)
            return new List<PlayerSearchHit>();

        var rows = await db.Users
            .Where(u => u.Username.StartsWith(query) &&
                (u.ProfileVisibility == ProfileVisibility.Public ||
                 (viewerLoggedIn && u.ProfileVisibility == ProfileVisibility.Members)))
            .OrderBy(u => u.Username)
            .Take(MaxSearchResults)
            .Select(u => new { u.Username, u.Xp })
            .ToListAsync();
        return rows.Select(u =>
        {
            var level = Progression.LevelFor(u.Xp);
            return new PlayerSearchHit(u.Username, level, Progression.TitleFor(level));
        }).ToList();
    }
}

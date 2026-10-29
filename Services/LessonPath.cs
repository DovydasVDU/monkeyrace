// Services/LessonPath.cs
// Pamokų kelio prizai: bananai už baigtą modulį ir už visas jo 3 žvaigždutes, plius prizai už įveiktų pamokų skaičių.
// Čia tik taisyklės; išdavimą (vienkartinį, per UserClaims) daro RaceService.SettleLessonPathAsync.
public static class LessonPath
{
    public sealed record Milestone(int Lessons, int Bananas, string ItemId);

    // 1/3, 1/2, 2/3 ir visos pamokų (iš 29); daiktai – ShopCatalog (ItemSource.Lesson)
    public static readonly Milestone[] Milestones =
    {
        new(10, 100, "bg-neon"),
        new(15, 100, "hat-keycap"),
        new(20, 150, "track-letters"),
        new(29, 300, "frame-neon")
    };

    public const int ModuleBananas = 40;
    public const int MasteryBananas = 100;
}

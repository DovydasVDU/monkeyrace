// Controllers/LessonsController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace monkeyrace.Controllers
{
    // Aklojo rašymo pamokos: žemėlapis /pamokos ir pamokos grotuvas /pamokos/{id}.
    // Pirmos LessonCatalog.FreeLessons pamokos prieinamos be paskyros (progresas naršyklėje), toliau reikia prisijungti.
    public class LessonsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly RaceService _raceService;

        public LessonsController(AppDbContext db, RaceService raceService)
        {
            _db = db;
            _raceService = raceService;
        }

        [HttpGet("/pamokos")]
        public async Task<IActionResult> Index()
        {
            var userId = PlayerIdentity.GetUserId(HttpContext);
            ViewBag.LoggedIn = userId != null;
            var progress = userId == null
                ? new Dictionary<string, UserLessonProgress>()
                : await _db.UserLessonProgresses.Where(p => p.UserId == userId.Value)
                    .ToDictionaryAsync(p => p.LessonId);
            return View(progress);
        }

        [HttpGet("/pamokos/{id}")]
        public async Task<IActionResult> Play(string id)
        {
            var lesson = LessonCatalog.Find(id);
            if (lesson == null)
                return NotFound();

            var userId = PlayerIdentity.GetUserId(HttpContext);
            ViewBag.Locked = userId == null && !LessonCatalog.IsFree(lesson);
            ViewBag.LoggedIn = userId != null;
            ViewBag.Next = LessonCatalog.Next(lesson);
            ViewBag.Module = LessonCatalog.ModuleOf(lesson);

            // prisijungusiam atrakinta, kai praėjo ankstesnę pamoką (bent 1 žvaigždutė)
            var best = new UserLessonProgress();
            if (userId != null)
            {
                var progress = await _db.UserLessonProgresses.Where(p => p.UserId == userId.Value).ToListAsync();
                best = progress.FirstOrDefault(p => p.LessonId == lesson.Id) ?? best;
                if (lesson.Index > 0)
                {
                    var previous = LessonCatalog.All[lesson.Index - 1];
                    if (!progress.Any(p => p.LessonId == previous.Id && p.Stars > 0))
                        return Redirect("/pamokos");
                }
            }
            ViewBag.BestStars = best.Stars;

            // vienas rezultatas vienam atidarymui; laikas naudojamas patikrinti, ar rezultatas įmanomas
            HttpContext.Session.SetString("LessonId", lesson.Id);
            HttpContext.Session.SetString("LessonStartedAt", DateTime.UtcNow.ToString("O"));
            return View(lesson);
        }

        // Kitas raundas. Serveris nieko neatsimena: visa būsena atkeliauja iš naršyklės.
        [HttpPost("/pamokos/{id}/round")]
        public async Task<IActionResult> Round(string id, [FromBody] LessonRoundDto dto)
        {
            var lesson = LessonCatalog.Find(id);
            if (lesson == null || dto == null)
                return BadRequest("Invalid payload.");
            if (!LessonCatalog.IsFree(lesson) && PlayerIdentity.GetUserId(HttpContext) == null)
                return Unauthorized();

            var keys = (dto.Keys ?? new Dictionary<string, KeyPerf>())
                .Where(pair => pair.Key.Length is >= 1 and <= 2 && pair.Value != null)
                .Take(80)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var recent = (dto.Recent ?? new List<string>()).Take(200).ToList();
            var pace = LessonEngine.Pace(dto.Pace);
            var level = Math.Clamp(dto.Level ?? lesson.StartLevel, 0, LessonEngine.MaxLevel);
            var roundsDone = Math.Clamp(dto.Round, 0, LessonEngine.MaxRounds);

            var sentences = new List<string>();
            if (lesson.Kind == LessonKind.Sentences)
            {
                var texts = await _db.TextSnippets.Where(t => t.SourceId != null)
                    .OrderBy(t => EF.Functions.Random()).Take(40).Select(t => t.Content).ToListAsync();
                sentences = LessonEngine.ExtractSentences(lesson, texts);
            }

            var plan = LessonEngine.NextRound(lesson, pace, roundsDone, level, dto.LastAccuracy, dto.LastWpm,
                keys, recent, sentences, Random.Shared);
            return Json(new { done = plan.Done, reason = plan.Reason, level = plan.Level, items = plan.Items, focus = plan.Focus });
        }

        // Pamokos pabaiga: žvaigždutės skaičiuojamos serveryje iš raundų statistikos
        [HttpPost("/pamokos/{id}/complete")]
        public async Task<IActionResult> Complete(string id, [FromBody] LessonCompleteDto dto)
        {
            var lesson = LessonCatalog.Find(id);
            if (lesson == null || dto?.Rounds == null)
                return BadRequest("Invalid payload.");
            var userId = PlayerIdentity.GetUserId(HttpContext);
            if (userId == null && !LessonCatalog.IsFree(lesson))
                return Unauthorized();

            var sessionLesson = HttpContext.Session.GetString("LessonId");
            var startedText = HttpContext.Session.GetString("LessonStartedAt");
            if (sessionLesson != lesson.Id || !DateTime.TryParse(startedText, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt))
                return BadRequest("Lesson unavailable.");
            HttpContext.Session.Remove("LessonId");
            HttpContext.Session.Remove("LessonStartedAt");

            if (dto.Rounds.Count is < 1 or > LessonEngine.MaxRounds || dto.Rounds.Any(r => r == null ||
                    r.Chars < 1 || r.Chars > 3000 || r.Errors < 0 || r.Errors > 6000 || r.ElapsedMs < 500))
                return BadRequest("Invalid payload.");

            var chars = dto.Rounds.Sum(r => r.Chars);
            var errors = dto.Rounds.Sum(r => r.Errors);
            var elapsedMs = dto.Rounds.Sum(r => r.ElapsedMs);
            // rašymo laikas negali būti ilgesnis už laiką nuo puslapio atidarymo
            if (elapsedMs > (DateTime.UtcNow - startedAt).TotalMilliseconds + 2000)
                return BadRequest("Implausible result.");
            var wpm = chars / 5d / (elapsedMs / 60000d);
            if (wpm > RaceService.MaxWpm)
                return BadRequest("Implausible result.");

            var accuracy = chars * 100d / (chars + errors);
            var pace = LessonEngine.Pace(dto.Pace);
            var stars = LessonEngine.StarsFor(lesson, pace, accuracy, wpm);
            var roundedWpm = (int)Math.Round(wpm);

            if (userId == null)
            {
                // svečias: parodome, kiek būtų gavęs, nieko nesaugome (progresas lieka naršyklėje)
                var reward = Progression.LessonReward(stars, dto.PreviousStars ?? 0, 0);
                return Json(new
                {
                    success = true, stars, accuracy, wpm = roundedWpm, saved = false,
                    reward = RaceReward.Create(0, reward.Xp, reward.Bananas, reward.Xp, reward.Bananas, false, false)
                });
            }

            var saved = await _raceService.SaveLessonResultAsync(userId.Value, lesson.Id, stars, accuracy, roundedWpm);
            return Json(new
            {
                success = true, stars, accuracy, wpm = roundedWpm, saved = true,
                bestStars = saved.BestStars, newStars = saved.NewStars, reward = saved.Reward
            });
        }

        // Prisijungus perkelia svečio progresą (tik nemokamos pamokos, be apdovanojimų)
        [HttpPost("/pamokos/sync")]
        public async Task<IActionResult> Sync([FromBody] List<GuestLessonDto> items)
        {
            var userId = PlayerIdentity.GetUserId(HttpContext);
            if (userId == null)
                return Unauthorized();
            if (items == null || items.Count > LessonCatalog.FreeLessons)
                return BadRequest("Invalid payload.");

            foreach (var item in items.Where(i => i != null))
            {
                var lesson = LessonCatalog.Find(item.Id);
                if (lesson == null || !LessonCatalog.IsFree(lesson) || item.Stars is < 1 or > 3)
                    continue;
                var row = await _db.UserLessonProgresses
                    .FirstOrDefaultAsync(p => p.UserId == userId.Value && p.LessonId == lesson.Id);
                if (row == null)
                    _db.UserLessonProgresses.Add(row = new UserLessonProgress
                    {
                        UserId = userId.Value, LessonId = lesson.Id, CompletedAt = DateTime.UtcNow
                    });
                row.Stars = Math.Max(row.Stars, item.Stars);
                row.BestAccuracy = Math.Max(row.BestAccuracy, Math.Clamp(item.Accuracy, 0, 100));
                row.BestWpm = Math.Max(row.BestWpm, Math.Clamp(item.Wpm, 0, RaceService.MaxWpm));
                row.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            return Ok();
        }
    }

    public class LessonRoundDto
    {
        public string Pace { get; set; }
        // kiek raundų jau sužaista
        public int Round { get; set; }
        public int? Level { get; set; }
        public double LastAccuracy { get; set; }
        public double LastWpm { get; set; }
        public Dictionary<string, KeyPerf> Keys { get; set; }
        public List<string> Recent { get; set; }
    }

    public class LessonCompleteDto
    {
        public string Pace { get; set; }
        public int? PreviousStars { get; set; }
        public List<LessonRoundStat> Rounds { get; set; }
    }

    public class LessonRoundStat
    {
        // teisingai paspausti simboliai
        public int Chars { get; set; }
        // neteisingi paspaudimai
        public int Errors { get; set; }
        public int ElapsedMs { get; set; }
    }

    public class GuestLessonDto
    {
        public string Id { get; set; }
        public int Stars { get; set; }
        public double Accuracy { get; set; }
        public int Wpm { get; set; }
    }
}

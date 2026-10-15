using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
//using monkeyrace.Data;

// This sets the base URL for this controller to: /api/StatsApi
namespace monkeyrace.Controllers
{
    [Route("api/stats")]
    [ApiController]
    public class StatsApiController : ControllerBase
    {
        private readonly AppDbContext _db;

        // Inject your database context
        private readonly RaceService _raceService;

        public StatsApiController(AppDbContext db, RaceService raceService)
        {
            _db = db;
            _raceService = raceService;
        }

        // This makes the endpoint: GET /api/StatsApi/{username}
        [HttpGet("{username}")]
        public async Task<IActionResult> GetUserStats(string username)
        {
            var userId = await _db.Users.Where(u => u.Username == username)
                .Select(u => (int?)u.Id).FirstOrDefaultAsync();
            if (userId == null)
                return NotFound(new { message = $"User '{username}' not found." });

            // ta pati kortelė kaip lenktynėse ir navbar (RaceService.GetPlayerCardAsync)
            return Ok(await _raceService.GetPlayerCardAsync(userId.Value));
        }

        [HttpGet("top10")]
        public async Task<IActionResult> GetTop10Races()
        {
            // 1. Fetch the top 10 best results from the database
            // Tik klasikinės online lenktynės: solo ir žodžių režimo WPM nepalyginami (trumpas spurtas duoda daugiau)
            // We order by WPM (highest first), then by Accuracy to break ties
            // Projekcija, kad nebūtų kraunamas ReplayJson (~10 KB vienoms lenktynėms)
            var topResults = await _db.RaceResults
                .Where(r => r.Race.IsMultiplayer && r.Race.DurationSeconds == null)
                .OrderByDescending(r => r.WPM)
                .ThenByDescending(r => r.Accuracy)
                .Take(10)
                .Select(r => new
                {
                    Username = r.User.Username,
                    r.WPM,
                    r.Accuracy,
                    r.Place,
                    r.Race.TextSnippetId,
                    r.Id,
                    r.CompletedAt,
                    HasReplay = r.Race.ReplayJson != null
                })
                .ToListAsync();

            // 2. Map the data and assign a "Rank" (1 through 10) dynamically
            var leaderboard = topResults.Select((result, index) => new
            {
                Rank = index + 1,
                Username = result.Username ?? "Unknown",
                Wpm = result.WPM,
                Accuracy = Math.Round(result.Accuracy, 2),
                Place = result.Place, // kelinta vieta tose lenktynėse; null seniems įrašams
                TextId = result.TextSnippetId,
                ResultId = result.Id,
                result.HasReplay, // /race/replay/{resultId}; false seniems įrašams
                Date = result.CompletedAt.ToString("yyyy-MM-dd")
            });

            // 3. Return the JSON array
            return Ok(leaderboard);
        }
    }
    
}

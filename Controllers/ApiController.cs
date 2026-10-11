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
            // We order by WPM (highest first), then by Accuracy to break ties
            var topResults = await _db.RaceResults
                .Include(r => r.User) // This tells Entity Framework to fetch the User info so we can get the Username
                .OrderByDescending(r => r.WPM)
                .ThenByDescending(r => r.Accuracy)
                .Take(10)
                .ToListAsync();

            // 2. Map the data and assign a "Place" (1 through 10) dynamically
            var leaderboard = topResults.Select((race, index) => new 
            {
                Place = index + 1,
                Username = race.User != null ? race.User.Username : "Unknown",
                Wpm = race.WPM,
                Accuracy = Math.Round(race.Accuracy, 2),
                Date = race.CompletedAt.ToString("yyyy-MM-dd")
            });

            // 3. Return the JSON array
            return Ok(leaderboard);
        }
    }
    
}

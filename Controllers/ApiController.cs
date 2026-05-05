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
        public StatsApiController(AppDbContext db)
        {
            _db = db;
        }

        // This makes the endpoint: GET /api/StatsApi/{username}
        [HttpGet("{username}")]
        public async Task<IActionResult> GetUserStats(string username)
        {
            // 1. Find the user by username
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            
            if (user == null)
            {
                return NotFound(new { message = $"User '{username}' not found." });
            }

            // 2. Go to RaceResults table and get all results belonging to this user
            var results = await _db.RaceResults
                                   .Where(r => r.UserId == user.Id)
                                   .ToListAsync();

            // 3. Calculate statistics
            int totalRaces = results.Count;
            int bestWpm = totalRaces > 0 ? results.Max(r => r.WPM) : 0;
            
            // Use Math.Round to keep the percentage clean (e.g., 98.45%)
            double avgAccuracy = totalRaces > 0 ? Math.Round(results.Average(r => r.Accuracy), 2) : 0;

            // 4. Return the data as JSON
            return Ok(new 
            {
                Username = user.Username,
                TotalRaces = totalRaces,
                BestWpm = bestWpm,
                AverageAccuracy = avgAccuracy
            });
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

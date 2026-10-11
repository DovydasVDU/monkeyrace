using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

public sealed class RaceHub : Hub
{
    private readonly MatchmakingService _matchmaking;
    private readonly ILogger<RaceHub> _logger;

    public RaceHub(
        MatchmakingService matchmaking,
        ILogger<RaceHub> logger)
    {
        _matchmaking = matchmaking;
        _logger = logger;
    }

    public async Task JoinRace(
        string raceId,
        string playerKey,
        string username,
        string tabId)
    {
        var expectedPlayerKey = PlayerIdentity.GetPlayerKey(Context.GetHttpContext());
        if (playerKey != expectedPlayerKey)
        {
            _logger.LogWarning(
                "Matchmaking room={RoomId} player={PlayerKey} decision=reject-identity-mismatch expected={ExpectedPlayerKey}",
                raceId, playerKey, expectedPlayerKey);
            await Clients.Caller.SendAsync("RaceUnavailable", "IdentityMismatch");
            return;
        }
        var result = await _matchmaking.JoinRaceAsync(
            raceId, playerKey, username, tabId, Context.ConnectionId);
        if (!result.IsSuccess)
        {
            await Clients.Caller.SendAsync("RaceUnavailable", result.Error);
            return;
        }

        await Clients.Caller.SendAsync("RaceState", result.State);
    }

    public Task SendProgress(
        string raceId, string playerKey, int correctChars,
        int totalKeystrokes, int wrongKeystrokes)
    {
        return _matchmaking.SendProgressAsync(
            raceId, playerKey, Context.ConnectionId, correctChars,
            totalKeystrokes, wrongKeystrokes);
    }

    public Task LeaveRace(string raceId, string playerKey)
    {
        var expectedPlayerKey = PlayerIdentity.GetPlayerKey(Context.GetHttpContext());
        if (playerKey != expectedPlayerKey)
        {
            _logger.LogWarning(
                "Matchmaking room={RoomId} player={PlayerKey} decision=reject-leave-identity-mismatch expected={ExpectedPlayerKey}",
                raceId, playerKey, expectedPlayerKey);
            return Task.CompletedTask;
        }
        return _matchmaking.LeaveAsync(
            raceId, playerKey, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(System.Exception exception)
    {
        await _matchmaking.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

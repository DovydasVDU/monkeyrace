using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

public static class PlayerIdentity
{
    public static string GetPlayerKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (httpContext.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(userId))
            return "user:" + userId;

        return "guest:" + httpContext.Session.Id;
    }

    public static int? GetUserId(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userId, out var parsed) ? parsed : (int?)null;
    }

    public static string GetUsername(HttpContext httpContext, string fallback = null)
    {
        return httpContext.User.FindFirstValue(ClaimTypes.Name) ??
            httpContext.Session.GetString("MatchmakingUsername") ??
            (string.IsNullOrWhiteSpace(fallback) ? "Svečias" : GuestName(fallback));
    }

    // Svečio vardas ateina iš adreso ir rodomas kitiems lenktynių žaidėjams
    public static string GuestName(string name)
    {
        name = name.Trim();
        return name.Substring(0, Math.Min(20, name.Length));
    }
}

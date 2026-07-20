using Microsoft.Extensions.Logging;
using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Singleton cache for PLK route data, keyed by date.
/// Survives across requests so the expensive full-schedule fetch happens at most once per date.
/// </summary>
public class RouteCache(IPlkTripService tripService, ILogger<RouteCache> logger)
{
    private readonly Dictionary<DateOnly, List<PlkRouteDto>> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<PlkRouteDto>> GetRoutesAsync(DateOnly date, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(date, out var cached))
        {
            logger.LogDebug("RouteCache: returning {RouteCount} cached routes for {Date}", cached.Count, date);
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check inside the lock
            if (_cache.TryGetValue(date, out cached))
                return cached;

            logger.LogDebug("RouteCache: loading routes for {Date}...", date);
            var routes = await tripService.GetAllRoutesAsync(date);

            var deduplicated = routes
                .GroupBy(r => new { r.ScheduleId, r.OrderId })
                .Select(g => g.First())
                .ToList();

            logger.LogDebug("RouteCache: loaded {RouteCount} routes for {Date} ({Duplicates} duplicates removed)",
                deduplicated.Count, date, routes.Count - deduplicated.Count);

            _cache[date] = deduplicated;
            return deduplicated;
        }
        finally
        {
            _lock.Release();
        }
    }
}

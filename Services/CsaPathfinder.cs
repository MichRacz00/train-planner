using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TrainPlanner.Models;
using Connection = TrainPlanner.Services.ConnectionBuilder.Connection;
using Journey = TrainPlanner.Services.CsaAlgorithm.Journey;

namespace TrainPlanner.Services;

public class CsaPathfinder(RouteCache routeCache, ILogger<CsaPathfinder> logger) : ITripPathfinder
{
    public async Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, TimeOnly departureAfter = default, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Starting search: {FromStationId} -> {ToStationId} on {TravelDate}", fromStationId, toStationId, travelDate);

        var routes = await routeCache.GetRoutesAsync(travelDate, ct);
        var connections = ConnectionBuilder.Build(routes, travelDate, logger, ct);

        // Walk forward through the day, one CSA call per distinct departure slot.
        // Keep the best (shortest-duration) journey per arrival minute.
        var best = new Dictionary<DateTime, Journey>();
        var seen = new HashSet<string>();
        var nextDep = departureAfter.ToTimeSpan();
        var dayEnd = TimeSpan.FromHours(24);

        while (nextDep < dayEnd)
        {
            var journey = CsaAlgorithm.FindEarliestArrival(connections, fromStationId, toStationId, nextDep);
            if (journey == null) break;

            nextDep = journey.Connections[0].DepartureTime + TimeSpan.FromTicks(1);

            var key = JourneyKey(journey);
            if (!seen.Add(key)) continue;

            TryAdd(best, journey, travelDate);
            logger.LogDebug("Candidate: dep {Dep}, arr {Arr}, {Transfers} transfers",
                ToTimeOnly(journey.Connections[0].DepartureTime),
                ToTimeOnly(journey.Connections[^1].ArrivalTime),
                journey.Transfers);
        }

        var results = best.Values
            .OrderBy(j => j.Connections[0].DepartureTime)
            .Select(BuildTrip)
            .Where(t => t != null)
            .Cast<MultiSegmentTrip>()
            .ToList();

        sw.Stop();
        logger.LogInformation("Complete: returned {ResultCount} results in {ElapsedMs}ms",
            results.Count, sw.ElapsedMilliseconds);

        return results;
    }

    private static string JourneyKey(Journey journey)
        => string.Join("|", journey.Connections.Select(c => $"{c.ScheduleId}:{c.OrderId}"));

    private static void TryAdd(Dictionary<DateTime, Journey> best, Journey journey, DateOnly travelDate)
    {
        var key = ArrivalKey(travelDate, journey.Connections[^1].ArrivalTime);
        var duration = journey.Connections[^1].ArrivalTime - journey.Connections[0].DepartureTime;

        if (!best.TryGetValue(key, out var existing))
        {
            best[key] = journey;
            return;
        }

        var existingDuration = existing.Connections[^1].ArrivalTime - existing.Connections[0].DepartureTime;
        if (duration < existingDuration)
            best[key] = journey;
    }

    private static DateTime ArrivalKey(DateOnly travelDate, TimeSpan rawArrival)
    {
        var dt = travelDate.ToDateTime(TimeOnly.MinValue) + rawArrival;
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
    }

    private static MultiSegmentTrip? BuildTrip(Journey journey)
    {
        if (journey.Connections.Count == 0)
            return null;

        var segments = journey.Connections.Select(c => new TripSegment(
            c.ScheduleId,
            c.OrderId,
            c.TrainName,
            c.CarrierCode,
            c.CommercialCategory,
            new PlkStation(c.FromStationId, "", ""),
            new PlkStation(c.ToStationId, "", ""),
            ToTimeOnly(c.DepartureTime),
            ToTimeOnly(c.ArrivalTime),
            c.DeparturePlatform,
            c.ArrivalPlatform)).ToList();

        var departure = journey.Connections[0].DepartureTime;
        var arrival = journey.Connections[^1].ArrivalTime;
        var totalDuration = arrival - departure;

        var transferTime = TimeSpan.Zero;
        for (var i = 1; i < journey.Connections.Count; i++)
            transferTime += journey.Connections[i].DepartureTime - journey.Connections[i - 1].ArrivalTime;

        return new MultiSegmentTrip(
            segments,
            ToTimeOnly(departure),
            ToTimeOnly(arrival),
            journey.Transfers,
            totalDuration,
            transferTime);
    }

    private static TimeOnly ToTimeOnly(TimeSpan ts)
    {
        var totalSeconds = (long)ts.TotalSeconds % (24L * 3600L);
        if (totalSeconds < 0) totalSeconds += 24L * 3600L;
        return TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds));
    }
}

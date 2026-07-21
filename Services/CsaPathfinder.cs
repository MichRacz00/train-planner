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

        // Find up to 4 routes departing at or after the requested time
        logger.LogInformation("Phase 3a: Finding after routes from {DepartureAfter}...", departureAfter);
        var afterJourneys = FindAfterJourneys(connections, fromStationId, toStationId, departureAfter.ToTimeSpan(), count: 4);
        logger.LogInformation("Phase 3a: found {Count} after routes", afterJourneys.Count);

        // Find up to 3 routes departing before the requested time
        logger.LogInformation("Phase 3b: Finding before routes before {DepartureAfter}...", departureAfter);
        var beforeJourneys = FindBeforeJourneys(connections, fromStationId, toStationId, departureAfter.ToTimeSpan(), count: 3);
        logger.LogInformation("Phase 3b: found {Count} before routes", beforeJourneys.Count);

        var results = MergeAndBuild(beforeJourneys, afterJourneys, maxResults: 7);

        sw.Stop();
        logger.LogInformation("Complete: returned {ResultCount} results ({BeforeCount} before, {AfterCount} after) in {ElapsedMs}ms",
            results.Count, beforeJourneys.Count, afterJourneys.Count, sw.ElapsedMilliseconds);

        return results;
    }

    private List<Journey> FindAfterJourneys(
        List<Connection> connections, int from, int to, TimeSpan startDep, int count)
    {
        var bestByArrivalHour = new Dictionary<int, Journey>();
        var seen = new HashSet<string>();
        var nextDep = startDep;
        var dayEnd = TimeSpan.FromHours(24);

        while (nextDep < dayEnd)
        {
            if (bestByArrivalHour.Count >= count)
            {
                var lastHourEnd = TimeSpan.FromHours(bestByArrivalHour.Keys.Max() + 1);
                if (nextDep >= lastHourEnd) break;
            }

            var journey = CsaAlgorithm.FindEarliestArrival(connections, from, to, nextDep);
            if (journey == null) break;

            nextDep = journey.Connections[0].DepartureTime + TimeSpan.FromTicks(1);

            var key = JourneyKey(journey);
            if (!seen.Add(key)) continue;

            TryAddToBestByArrivalHour(bestByArrivalHour, journey);
            logger.LogDebug("After candidate: dep {DepartureTime}, arr {ArrivalTime}, {Transfers} transfers",
                ToTimeOnly(journey.Connections[0].DepartureTime),
                ToTimeOnly(journey.Connections[^1].ArrivalTime),
                journey.Transfers);
        }

        return bestByArrivalHour.Values.ToList();
    }

    private List<Journey> FindBeforeJourneys(
        List<Connection> connections, int from, int to, TimeSpan cutoff, int count)
    {
        var bestByArrivalHour = new Dictionary<int, Journey>();
        var seen = new HashSet<string>();
        var beforeCutoff = cutoff;

        while (true)
        {
            if (bestByArrivalHour.Count >= count)
            {
                var firstHourStart = TimeSpan.FromHours(bestByArrivalHour.Keys.Min());
                if (beforeCutoff <= firstHourStart) break;
            }

            var latestDep = connections
                .Where(c => c.FromStationId == from && c.DepartureTime < beforeCutoff)
                .Select(c => c.DepartureTime)
                .DefaultIfEmpty(TimeSpan.MinValue)
                .Max();

            if (latestDep == TimeSpan.MinValue) break;

            var journey = CsaAlgorithm.FindEarliestArrival(connections, from, to, latestDep);
            if (journey == null) break;
            if (journey.Connections[0].DepartureTime >= beforeCutoff)
            {
                beforeCutoff = latestDep;
                continue;
            }

            beforeCutoff = journey.Connections[0].DepartureTime;

            var key = JourneyKey(journey);
            if (!seen.Add(key)) continue;

            TryAddToBestByArrivalHour(bestByArrivalHour, journey);
            logger.LogDebug("Before candidate: dep {DepartureTime}, arr {ArrivalTime}, {Transfers} transfers",
                ToTimeOnly(journey.Connections[0].DepartureTime),
                ToTimeOnly(journey.Connections[^1].ArrivalTime),
                journey.Transfers);
        }

        return bestByArrivalHour.Values.ToList();
    }

    private static void TryAddToBestByArrivalHour(Dictionary<int, Journey> best, Journey journey)
    {
        var arrivalHour = (int)journey.Connections[^1].ArrivalTime.TotalHours;
        var duration = journey.Connections[^1].ArrivalTime - journey.Connections[0].DepartureTime;

        if (!best.TryGetValue(arrivalHour, out var existing))
        {
            best[arrivalHour] = journey;
            return;
        }

        var existingDuration = existing.Connections[^1].ArrivalTime - existing.Connections[0].DepartureTime;
        if (duration < existingDuration)
            best[arrivalHour] = journey;
    }

    private static List<MultiSegmentTrip> MergeAndBuild(
        List<Journey> before, List<Journey> after, int maxResults)
    {
        var seen = new HashSet<string>();
        var results = new List<MultiSegmentTrip>();

        foreach (var journey in before.Concat(after))
        {
            if (results.Count >= maxResults) break;

            var key = JourneyKey(journey);
            if (!seen.Add(key)) continue;

            var trip = BuildTrip(journey);
            if (trip != null) results.Add(trip);
        }

        results.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));
        return results;
    }

    private static string JourneyKey(Journey journey)
        => string.Join("|", journey.Connections.Select(c => $"{c.ScheduleId}:{c.OrderId}"));

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

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

        var journeys = new List<Journey>();
        var nextDep = departureAfter.ToTimeSpan();
        var midnight = TimeSpan.FromHours(24);

        while (nextDep < midnight)
        {
            var journey = CsaAlgorithm.FindEarliestArrival(connections, fromStationId, toStationId, nextDep);
            if (journey == null) break;

            nextDep = journey.Connections[0].DepartureTime + TimeSpan.FromTicks(1);
            journeys.Add(journey);
        }

        var results = journeys
            .GroupBy(GetTypeKey)
            .SelectMany(DeduplicateByArrivalMinute)
            .OrderBy(j => j.Connections[0].DepartureTime)
            .Select(BuildTrip)
            .Where(t => t != null)
            .Cast<MultiSegmentTrip>()
            .ToList();

        logger.LogInformation("Complete: {Raw} journeys -> {Result} after type-dedup in {ElapsedMs}ms",
            journeys.Count, results.Count, sw.ElapsedMilliseconds);

        sw.Stop();
        return results;
    }

    private static string GetTypeKey(Journey journey)
    {
        var result = new List<string>();
        string? lastTrain = null;
        foreach (var conn in journey.Connections)
        {
            var train = $"{conn.ScheduleId}:{conn.OrderId}";
            if (train != lastTrain)
            {
                var cat = string.IsNullOrEmpty(conn.CommercialCategory) ? "?" : conn.CommercialCategory;
                result.Add(cat);
                lastTrain = train;
            }
        }
        Console.WriteLine(string.Join("→", result));
        return string.Join("→", result);
    }

    private static IEnumerable<Journey> DeduplicateByArrivalMinute(IEnumerable<Journey> group)
    {
        return group
            .GroupBy(j => (long)j.Connections[^1].ArrivalTime.TotalMinutes)
            .Select(bucket => bucket.MaxBy(j => j.Connections[0].DepartureTime)!);
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

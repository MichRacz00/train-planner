using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TrainPlanner.Models;

namespace TrainPlanner.Services;

public class CsaPathfinder(RouteCache routeCache, ILogger<CsaPathfinder> logger) : ITripPathfinder
{
    public async Task<IReadOnlyList<Journey>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, TimeOnly departureAfter = default, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Starting search: {FromStationId} -> {ToStationId} on {TravelDate}", fromStationId, toStationId, travelDate);

        var routes = await routeCache.GetRoutesAsync(travelDate, ct);
        var legs = ConnectionBuilder.Build(routes, travelDate, logger, ct);

        var trips = new List<Journey>();
        var nextDep = travelDate.ToDateTime(departureAfter);
        var midnight = travelDate.ToDateTime(TimeOnly.MinValue).AddDays(1);

        while (nextDep < midnight)
        {
            var trip = CsaAlgorithm.FindEarliestArrival(legs, fromStationId, toStationId, nextDep);
            if (trip == null) break;

            nextDep = trip.Departure + TimeSpan.FromTicks(1);
            trips.Add(trip);
            Console.WriteLine(trip);
        }

        var results = trips
            .GroupBy(GetTypeKey)
            .SelectMany(DeduplicateByArrivalMinute)
            .OrderBy(t => t.Departure)
            .ToList();

        logger.LogInformation("Complete: {Raw} trips -> {Result} after type-dedup in {ElapsedMs}ms",
            trips.Count, results.Count, sw.ElapsedMilliseconds);

        sw.Stop();
        return results;
    }

    private static string GetTypeKey(Journey trip)
    {
        var result = new List<string>();
        string? lastTrain = null;
        foreach (var leg in trip.Legs)
        {
            var train = $"{leg.ScheduleId}:{leg.OrderId}";
            if (train != lastTrain)
            {
                var cat = string.IsNullOrEmpty(leg.CommercialCategory) ? "?" : leg.CommercialCategory;
                result.Add(cat);
                lastTrain = train;
            }
        }
        return string.Join("→", result);
    }

    private static IEnumerable<Journey> DeduplicateByArrivalMinute(IEnumerable<Journey> group)
    {
        return group
            .GroupBy(t => (long)t.Arrival.TimeOfDay.TotalMinutes)
            .Select(bucket => bucket.MaxBy(t => t.Departure)!);
    }
}

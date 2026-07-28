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
            var newTrips = CsaAlgorithm.FindJourneys(legs, fromStationId, toStationId, nextDep);
            if (newTrips.Count == 0) break; //TODO, fetch connections for next day somehow, keep in mind edge cases
            
            nextDep = newTrips
                .Min(x => x.Departure)
                .AddTicks(1);
            trips.AddRange(newTrips);
        }
        
        var results = ParetoFilter(trips)
            .OrderBy(t => t.Departure)
            .ToList();

        logger.LogInformation("Complete: {Raw} trips -> {Result} after Pareto filter in {ElapsedMs}ms",
            trips.Count, results.Count, sw.ElapsedMilliseconds);

        sw.Stop();
        return results;
    }

    private static IEnumerable<Journey> ParetoFilter(IEnumerable<Journey> journeys)
    {
        // Keep a journey only if its transfer count is strictly less than the minimum
        // seen so far among all journeys with an earlier or equal arrival.
        // Scanning by ascending arrival means each survivor represents a genuine
        // trade-off: it arrives later but requires fewer transfers than everything faster.
        var bestTransfersSoFar = int.MaxValue;
        foreach (var journey in journeys.OrderBy(j => j.Arrival))
        {
            if (journey.Transfers < bestTransfersSoFar)
            {
                bestTransfersSoFar = journey.Transfers;
                yield return journey;
            }
        }
    }
}

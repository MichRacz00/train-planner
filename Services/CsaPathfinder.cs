using System.Diagnostics;
using train_planner.Models;

namespace train_planner.Services;

public class CsaPathfinder(IPlkTripService tripService) : ITripPathfinder
{
    private readonly IPlkTripService _tripService = tripService;
    private List<PlkRouteDto> _allRoutes = new();

    // Convert TimeSpan to TimeOnly, wrapping times > 24 hours back to 0-24 range
    private static TimeOnly ToTimeOnly(TimeSpan ts)
    {
        var totalSeconds = (long)ts.TotalSeconds % (24L * 3600L);
        if (totalSeconds < 0) totalSeconds += 24L * 3600L;
        return TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds));
    }

    // A single connection: one train segment between two stations
    private sealed class Connection
    {
        public int FromStationId { get; init; }
        public int ToStationId { get; init; }
        public TimeSpan DepartureTime { get; init; }
        public TimeSpan ArrivalTime { get; init; }
        public int ScheduleId { get; init; }
        public int OrderId { get; init; }
        public string TrainName { get; init; } = "";
        public string CarrierCode { get; init; } = "";
        public string CommercialCategory { get; init; } = "";
        public string? DeparturePlatform { get; init; }
        public string? ArrivalPlatform { get; init; }
    }

    // Label: state at a station during CSA scan
    private sealed class Label
    {
        public TimeSpan Arrival { get; init; }
        public Connection LastConnection { get; init; } = null!;
    }

    // Journey result: list of connections taken and transfer count
    private sealed record Journey(
        List<Connection> Connections,
        int Transfers);

    private async Task<List<PlkRouteDto>> GetAllRoutes(DateOnly travelDate)
    {
        if (_allRoutes.Count == 0)
        {
            Console.WriteLine($"[CSA] Phase 1: Loading routes...");
            var routes = await _tripService.GetAllRoutesAsync(travelDate);
            Console.WriteLine($"[CSA] Phase 1: Loaded {routes.Count} routes");
            _allRoutes = routes
                .GroupBy(r => new { r.ScheduleId, r.OrderId })
                .Select(g => g.First())
                .ToList();
            Console.WriteLine($"[CSA] Phase 1: After deduplication: {_allRoutes.Count} routes");
        }
        else
        {
            Console.WriteLine($"[CSA] Phase 1: Loaded from cache {_allRoutes.Count} routes");
        }
        
        return _allRoutes;
    }
    
    public async Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, TimeOnly departureAfter = default, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[CSA] Starting search: {fromStationId} -> {toStationId} on {travelDate}");

        // Phase 1: Load all routes and flatten into connections
        var routes = await GetAllRoutes(travelDate);

        var connections = new List<Connection>();
        var routesProcessed = 0;
        var routesSkipped = 0;

        foreach (var route in routes)
        {
            ct.ThrowIfCancellationRequested();

            if (route.Stations is not { Count: >= 2 })
            {
                routesSkipped++;
                continue;
            }

            if (!OperatesOnDate(route, travelDate))
            {
                routesSkipped++;
                continue;
            }

            routesProcessed++;

            // Flatten route into connections (one per consecutive station pair)
            var connectionsAdded = 0;
            for (var i = 0; i < route.Stations.Count - 1; i++)
            {
                var from = route.Stations[i];
                var to = route.Stations[i + 1];

                if (from.DepartureTime is null || to.ArrivalTime is null)
                    continue;

                if (!TryParseTime(from.DepartureTime, out var depTime))
                    continue;
                if (!TryParseTime(to.ArrivalTime, out var arrTime))
                    continue;

                var depDay = from.DepartureDay ?? 0;
                var arrDay = to.ArrivalDay ?? 0;
                var depTimeSpan = depTime.ToTimeSpan() + TimeSpan.FromHours(24 * depDay);
                var arrTimeSpan = arrTime.ToTimeSpan() + TimeSpan.FromHours(24 * arrDay);

                connections.Add(new Connection
                {
                    FromStationId = from.StationId,
                    ToStationId = to.StationId,
                    DepartureTime = depTimeSpan,
                    ArrivalTime = arrTimeSpan,
                    ScheduleId = route.ScheduleId,
                    OrderId = route.OrderId,
                    TrainName = route.Name ?? route.NationalNumber ?? $"{route.CarrierCode} {route.OrderId}",
                    CarrierCode = route.CarrierCode ?? "",
                    CommercialCategory = route.CommercialCategorySymbol ?? "",
                    DeparturePlatform = from.DeparturePlatform,
                    ArrivalPlatform = to.ArrivalPlatform
                });
                connectionsAdded++;
            }

            // Progress update every 50 routes
            if (routesProcessed % 50 == 0)
                Console.WriteLine($"[CSA] Progress: {routesProcessed} routes processed, {connections.Count} total connections so far");

            if (connectionsAdded > 0)
                Console.WriteLine($"[CSA] Route {route.ScheduleId}/{route.OrderId}: {connectionsAdded} connections");
        }

        Console.WriteLine($"[CSA] Phase 1 complete: {routesProcessed} routes processed, {routesSkipped} skipped, {connections.Count} total connections");

        // Phase 2: Sort connections by departure time (CSA requirement)
        Console.WriteLine($"[CSA] Phase 2: Sorting {connections.Count} connections by departure time...");
        connections.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));
        Console.WriteLine($"[CSA] Phase 2: Sort complete");

        // Phase 3a: Find up to 4 "after" routes
        Console.WriteLine($"[CSA] Phase 3a: Finding after routes from {departureAfter}...");
        var afterJourneys = new List<Journey>();
        var nextDep = departureAfter.ToTimeSpan();
        for (var i = 0; i < 4; i++)
        {
            var journey = RunSingleScan(connections, fromStationId, toStationId, nextDep);
            if (journey == null) break;
            afterJourneys.Add(journey);
            nextDep = journey.Connections[0].DepartureTime + TimeSpan.FromTicks(1);
            Console.WriteLine($"[CSA] After route {i + 1}: dep {ToTimeOnly(journey.Connections[0].DepartureTime):HH:mm}, {journey.Transfers} transfers");
        }
        Console.WriteLine($"[CSA] Phase 3a: found {afterJourneys.Count} after routes");

        // Phase 3b: Find up to 3 "before" routes
        Console.WriteLine($"[CSA] Phase 3b: Finding before routes before {departureAfter}...");
        var beforeJourneys = new List<Journey>();
        var beforeCutoff = departureAfter.ToTimeSpan();
        for (var i = 0; i < 3; i++)
        {
            var latestDep = connections
                .Where(c => c.FromStationId == fromStationId && c.DepartureTime < beforeCutoff)
                .Select(c => c.DepartureTime)
                .DefaultIfEmpty(TimeSpan.MinValue)
                .Max();
            if (latestDep == TimeSpan.MinValue) break;

            var journey = RunSingleScan(connections, fromStationId, toStationId, latestDep);
            if (journey == null || journey.Connections[0].DepartureTime >= beforeCutoff) break;
            beforeJourneys.Add(journey);
            beforeCutoff = journey.Connections[0].DepartureTime;
            Console.WriteLine($"[CSA] Before route {i + 1}: dep {ToTimeOnly(journey.Connections[0].DepartureTime):HH:mm}, {journey.Transfers} transfers");
        }
        Console.WriteLine($"[CSA] Phase 3b: found {beforeJourneys.Count} before routes");

        // Phase 4: Merge, deduplicate, order, return up to 7
        Console.WriteLine($"[CSA] Phase 4: Merging and deduplicating results...");
        var seenRoutes = new HashSet<string>();
        var results = new List<MultiSegmentTrip>();

        var allJourneys = beforeJourneys.Concat(afterJourneys);
        foreach (var journey in allJourneys)
        {
            if (results.Count >= 7) break;

            var routeKey = GetJourneyKey(journey);
            if (seenRoutes.Contains(routeKey)) continue;

            var trip = BuildTrip(journey);
            if (trip == null) continue;

            seenRoutes.Add(routeKey);
            results.Add(trip);
        }

        results = results.OrderBy(t => t.DepartureTime).ToList();

        sw.Stop();
        Console.WriteLine($"[CSA] Complete: returned {results.Count} results ({beforeJourneys.Count} before, {afterJourneys.Count} after) in {sw.ElapsedMilliseconds}ms");

        return results;
    }

    /// <summary>
    /// Runs a single CSA scan from a given earliest departure time, returning the earliest-arrival journey.
    /// </summary>
    private static Journey? RunSingleScan(
        List<Connection> sortedConnections,
        int fromStationId,
        int toStationId,
        TimeSpan earliestDeparture)
    {
        var labels = new Dictionary<int, Label>();
        var predecessors = new Dictionary<Connection, Connection?>();

        labels[fromStationId] = new Label
        {
            Arrival = earliestDeparture,
            LastConnection = null!
        };

        foreach (var conn in sortedConnections)
        {
            if (!labels.TryGetValue(conn.FromStationId, out var label))
                continue;

            if (label.Arrival > conn.DepartureTime)
                continue;

            if (labels.TryGetValue(conn.ToStationId, out var current) && conn.ArrivalTime >= current.Arrival)
                continue;

            predecessors[conn] = label.LastConnection;

            labels[conn.ToStationId] = new Label
            {
                Arrival = conn.ArrivalTime,
                LastConnection = conn
            };

            if (conn.ToStationId == toStationId)
            {
                return ReconstructJourney(conn, predecessors);
            }
        }

        return null;
    }

    /// <summary>
    /// Reconstructs the journey by walking backwards through the predecessors mapping.
    /// </summary>
    private static Journey ReconstructJourney(Connection finalConnection, Dictionary<Connection, Connection?> predecessors)
    {
        var path = new List<Connection>();

        // Walk backwards from final connection to origin
        for (var c = finalConnection; c != null; c = predecessors.TryGetValue(c, out var prev) ? prev : null)
            path.Add(c);

        // Reverse to get chronological order
        path.Reverse();

        Console.WriteLine($"[CSA] Reconstructing journey with {path.Count} connections");

        // Count train changes (transfers)
        var transfers = 0;
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].ScheduleId != path[i - 1].ScheduleId ||
                path[i].OrderId != path[i - 1].OrderId)
            {
                transfers++;
                Console.WriteLine($"[CSA] Transfer at station {path[i].FromStationId}: {path[i - 1].TrainName} -> {path[i].TrainName}");
            }
        }

        Console.WriteLine($"[CSA] Journey reconstruction complete: {path.Count} connections, {transfers} transfers");

        return new Journey(path, transfers);
    }

    /// <summary>
    /// Get deduplication key for a journey (route composition).
    /// </summary>
    private static string GetJourneyKey(Journey journey)
    {
        return string.Join("|", journey.Connections.Select(c => $"{c.ScheduleId}:{c.OrderId}"));
    }

    /// <summary>
    /// Builds a MultiSegmentTrip from a journey (list of connections).
    /// </summary>
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
        {
            var gap = journey.Connections[i].DepartureTime - journey.Connections[i - 1].ArrivalTime;
            transferTime += gap;
        }

        return new MultiSegmentTrip(
            segments, 
            ToTimeOnly(departure), 
            ToTimeOnly(arrival), 
            journey.Transfers, totalDuration, transferTime);
    }

    private static bool OperatesOnDate(PlkRouteDto route, DateOnly date)
    {
        var dates = route.OperatingDates;
        if (dates is null || dates.Count == 0)
            return true;
        return dates.Contains(date);
    }

    private static bool TryParseTime(string? raw, out TimeOnly time)
    {
        time = TimeOnly.MinValue;
        if (string.IsNullOrEmpty(raw))
            return false;
        if (TimeOnly.TryParse(raw, out time))
            return true;
        if (TimeSpan.TryParse(raw, out var ts))
        {
            time = TimeOnly.FromTimeSpan(ts);
            return true;
        }
        return false;
    }
}

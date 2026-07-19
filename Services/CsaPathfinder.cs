using System.Diagnostics;
using train_planner.Models;

namespace train_planner.Services;

public class CsaPathfinder(IPlkTripService tripService) : ITripPathfinder
{
    private readonly IPlkTripService _tripService = tripService;

    // A single connection: one train segment between two stations
    private sealed class Connection
    {
        public int FromStationId { get; init; }
        public int ToStationId { get; init; }
        public TimeOnly DepartureTime { get; init; }
        public TimeOnly ArrivalTime { get; init; }
        public int ScheduleId { get; init; }
        public int OrderId { get; init; }
        public string TrainName { get; init; } = "";
        public string CarrierCode { get; init; } = "";
        public string CommercialCategory { get; init; } = "";
        public string? DeparturePlatform { get; init; }
        public string? ArrivalPlatform { get; init; }
    }

    // Journey result: list of connections taken and transfer count
    private sealed record Journey(
        List<Connection> Connections,
        int Transfers);

    public async Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[CSA] Starting search: {fromStationId} -> {toStationId} on {travelDate}");

        // Phase 1: Load all routes and flatten into connections
        Console.WriteLine($"[CSA] Phase 1: Loading routes...");
        var routes = await _tripService.GetAllRoutesAsync(travelDate);
        Console.WriteLine($"[CSA] Phase 1: Loaded {routes.Count} routes");

        // Deduplicate routes by ScheduleId/OrderId
        routes = routes
            .GroupBy(r => new { r.ScheduleId, r.OrderId })
            .Select(g => g.First())
            .ToList();
        Console.WriteLine($"[CSA] Phase 1: After deduplication: {routes.Count} routes");

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

                connections.Add(new Connection
                {
                    FromStationId = from.StationId,
                    ToStationId = to.StationId,
                    DepartureTime = depTime,
                    ArrivalTime = arrTime,
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

        // Phase 3: CSA scan - single pass through sorted connections
        Console.WriteLine($"[CSA] Phase 3: Starting CSA scan...");
        var bestArrival = new Dictionary<int, TimeOnly>();
        var bestConnection = new Dictionary<int, Connection>();
        var predecessors = new Dictionary<Connection, Connection?>();
        var connectionsEvaluated = 0;
        var connectionsBoarded = 0;
        var connectionsSkipped = 0;

        // Initialize: we can start at origin at any time
        bestArrival[fromStationId] = TimeOnly.MinValue;
        Console.WriteLine($"[CSA] Initialized arrival at station {fromStationId} at {TimeOnly.MinValue}");

        foreach (var conn in connections)
        {
            // Progress update every 1000 connections
            if (connectionsEvaluated % 1000 == 0)
                Console.WriteLine($"[CSA] Scan progress: {connectionsEvaluated}/{connections.Count} connections evaluated, {connectionsBoarded} boarded");

            connectionsEvaluated++;

            // Can we board this connection?
            if (!bestArrival.TryGetValue(conn.FromStationId, out var arrivalAtFrom))
            {
                connectionsSkipped++;
                continue; // We haven't reached the boarding station yet
            }

            if (arrivalAtFrom > conn.DepartureTime)
            {
                connectionsSkipped++;
                continue; // We arrived after this train departed
            }

            // Is this an improvement?
            if (bestArrival.TryGetValue(conn.ToStationId, out var currentBest) && conn.ArrivalTime >= currentBest)
            {
                Console.WriteLine($"[CSA] Arrival at {conn.ToStationId} at {conn.ArrivalTime:HH:mm} is worse than existing {currentBest:HH:mm}, skipping");
                continue; // We already have a better (earlier) arrival at destination
            }

            // Capture the exact predecessor chain
            bestConnection.TryGetValue(conn.FromStationId, out var prevConn);
            predecessors[conn] = prevConn;

            // We can board! Update best arrival and connection
            bestArrival[conn.ToStationId] = conn.ArrivalTime;
            bestConnection[conn.ToStationId] = conn;

            connectionsBoarded++;
            Console.WriteLine($"[CSA] Boarded connection: {conn.TrainName} {conn.FromStationId}@{conn.DepartureTime:HH:mm} -> {conn.ToStationId}@{conn.ArrivalTime:HH:mm}");
        }

        Console.WriteLine($"[CSA] Phase 3 complete: evaluated {connectionsEvaluated}, boarded {connectionsBoarded}, skipped {connectionsSkipped}");

        // Reconstruct the earliest-arrival journey
        if (!bestConnection.TryGetValue(toStationId, out var finalConn))
        {
            Console.WriteLine($"[CSA] No path found to destination");
            sw.Stop();
            Console.WriteLine($"[CSA] Complete: returned 0 results in {sw.ElapsedMilliseconds}ms");
            return [];
        }

        var journey = ReconstructJourney(finalConn, predecessors);
        var trip = BuildTrip(journey);

        sw.Stop();
        Console.WriteLine($"[CSA] Complete: returned {(trip is not null ? 1 : 0)} results in {sw.ElapsedMilliseconds}ms");

        return trip is not null ? [trip] : [];
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
            c.DepartureTime,
            c.ArrivalTime,
            c.DeparturePlatform,
            c.ArrivalPlatform)).ToList();

        var departure = segments[0].PlannedDeparture;
        var arrival = segments[^1].PlannedArrival;

        var totalDuration = arrival > departure
            ? arrival - departure
            : TimeSpan.FromHours(24) - (departure - arrival);

        var transferTime = TimeSpan.Zero;
        for (var i = 1; i < segments.Count; i++)
        {
            var gap = segments[i].PlannedDeparture > segments[i - 1].PlannedArrival
                ? segments[i].PlannedDeparture - segments[i - 1].PlannedArrival
                : TimeSpan.FromHours(24) - (segments[i - 1].PlannedArrival - segments[i].PlannedDeparture);
            transferTime += gap;
        }

        return new MultiSegmentTrip(
            segments, departure, arrival, journey.Transfers, totalDuration, transferTime);
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

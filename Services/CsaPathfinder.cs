using train_planner.Models;

namespace train_planner.Services;

public class CsaPathfinder(IPlkTripService tripService) : ITripPathfinder
{
    private readonly IPlkTripService _tripService = tripService;

    private static readonly TimeSpan MinTransferTime = TimeSpan.FromMinutes(5);
    private const int MaxTransfers = 3;
    private const int MaxResults = 20;

    // Route info with pre-parsed station stops
    private sealed class RouteData
    {
        public int ScheduleId { get; }
        public int OrderId { get; }
        public string TrainName { get; }
        public string CarrierCode { get; }
        public string CommercialCategory { get; }
        public StationStop[] Stops { get; }

        public RouteData(PlkRouteDto route)
        {
            ScheduleId = route.ScheduleId;
            OrderId = route.OrderId;
            TrainName = route.Name ?? route.NationalNumber ?? $"{route.CarrierCode} {route.OrderId}";
            CarrierCode = route.CarrierCode ?? "";
            CommercialCategory = route.CommercialCategorySymbol ?? "";

            if (route.Stations is { Count: > 0 })
            {
                Stops = route.Stations
                    .Where(s => s.DepartureTime is not null || s.ArrivalTime is not null)
                    .Select((s, i) => new StationStop(s, i))
                    .ToArray();
            }
            else
            {
                Stops = [];
            }
        }
    }

    private sealed class StationStop(PlkStationOnRouteDto dto, int index)
    {
        public int StationId => dto.StationId;
        public int Index => index;
        public TimeOnly? Departure => TryParse(dto.DepartureTime);
        public TimeOnly? Arrival => TryParse(dto.ArrivalTime);
        public string? DeparturePlatform => dto.DeparturePlatform;
        public string? ArrivalPlatform => dto.ArrivalPlatform;
    }

    // A single ride on a train: board at boardStopIndex, alight at alightStopIndex
    private sealed record Ride(
        RouteData Route,
        int BoardStopIndex,
        int AlightStopIndex,
        TimeOnly BoardTime,
        TimeOnly AlightTime,
        int TransfersUsed,
        List<Ride> PreviousRides);

    public async Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, CancellationToken ct = default)
    {
        // Phase 1: Load all route data for the date (single API call)
        var routes = await _tripService.GetAllRoutesAsync(travelDate);

        var routesByStation = new Dictionary<int, List<RouteData>>();

        foreach (var route in routes)
        {
            ct.ThrowIfCancellationRequested();

            if (route.Stations is not { Count: >= 2 })
                continue;

            if (!OperatesOnDate(route, travelDate))
                continue;

            var data = new RouteData(route);
            if (data.Stops.Length < 2)
                continue;

            foreach (var stop in data.Stops)
            {
                if (!routesByStation.TryGetValue(stop.StationId, out var list))
                {
                    list = new List<RouteData>();
                    routesByStation[stop.StationId] = list;
                }
                list.Add(data);
            }
        }

        // Phase 2: CSA scan
        // Priority queue: items ordered by arrival time at current station
        // Each item: "I arrived at station X at time T on route R, boarded at stopIndex B"
        var pq = new PriorityQueue<(int StationId, TimeOnly ArriveTime, RouteData Route,
            int BoardStopIndex, int TransfersUsed, List<Ride> PreviousRides), TimeOnly>();

        var bestArrival = new Dictionary<int, TimeOnly>();
        var results = new List<List<Ride>>();

        // Seed: board every train departing from origin
        if (routesByStation.TryGetValue(fromStationId, out var originRoutes))
        {
            foreach (var route in originRoutes)
            {
                for (var i = 0; i < route.Stops.Length; i++)
                {
                    var stop = route.Stops[i];
                    if (stop.StationId != fromStationId) continue;
                    if (stop.Departure is not { } depTime) continue;
                    if (i + 1 >= route.Stops.Length) continue;

                    pq.Enqueue((fromStationId, depTime, route, i, 0, []), depTime);
                    break; // Only first departure from origin per route
                }
            }
        }

        // Phase 3: Scan loop
        while (pq.Count > 0 && results.Count < MaxResults * 4)
        {
            var (stationId, arriveTime, route, boardStopIndex, transfersUsed, prevRides) = pq.Dequeue();

            // Ride the train forward stop by stop
            for (var si = boardStopIndex + 1; si < route.Stops.Length; si++)
            {
                var stop = route.Stops[si];
                if (stop.Arrival is not { } arrTime) continue;

                // Prune: if we already reached this station earlier, skip
                if (bestArrival.TryGetValue(stop.StationId, out var best) && arrTime >= best)
                    continue;

                bestArrival[stop.StationId] = arrTime;

                // Record the completed ride
                var ride = new Ride(route, boardStopIndex, si, arriveTime, arrTime,
                    transfersUsed, prevRides);

                if (stop.StationId == toStationId)
                {
                    var completedPath = new List<Ride>(prevRides) { ride };
                    results.Add(completedPath);
                    continue; // Don't transfer at destination
                }

                // Try transfers at this station
                if (transfersUsed < MaxTransfers &&
                    routesByStation.TryGetValue(stop.StationId, out var transferRoutes))
                {
                    var earliestDeparture = arrTime.Add(MinTransferTime);

                    foreach (var tr in transferRoutes)
                    {
                        if (tr.ScheduleId == route.ScheduleId && tr.OrderId == route.OrderId)
                            continue;

                        for (var j = 0; j < tr.Stops.Length; j++)
                        {
                            var tStop = tr.Stops[j];
                            if (tStop.StationId != stop.StationId) continue;
                            if (tStop.Departure is not { } trDep) continue;
                            if (trDep < earliestDeparture) continue;
                            if (j + 1 >= tr.Stops.Length) continue;

                            var newPrev = new List<Ride>(prevRides) { ride };
                            pq.Enqueue((stop.StationId, trDep, tr, j, transfersUsed + 1, newPrev), trDep);
                            break;
                        }
                    }
                }
            }
        }

        // Phase 4: Assemble results
        var trips = new List<MultiSegmentTrip>();
        foreach (var path in results)
        {
            var trip = BuildTrip(path);
            if (trip is not null)
                trips.Add(trip);
        }

        return trips
            .OrderBy(t => t.TotalDuration)
            .ThenBy(t => t.Transfers)
            .Take(MaxResults)
            .ToArray();
    }

    private static MultiSegmentTrip? BuildTrip(List<Ride> rides)
    {
        if (rides.Count == 0) return null;

        var segments = new List<TripSegment>();
        foreach (var ride in rides)
        {
            var boardStop = ride.Route.Stops[ride.BoardStopIndex];
            var alightStop = ride.Route.Stops[ride.AlightStopIndex];

            segments.Add(new TripSegment(
                ride.Route.ScheduleId,
                ride.Route.OrderId,
                ride.Route.TrainName,
                ride.Route.CarrierCode,
                ride.Route.CommercialCategory,
                new PlkStation(boardStop.StationId, "", ""),
                new PlkStation(alightStop.StationId, "", ""),
                ride.BoardTime,
                ride.AlightTime,
                boardStop.DeparturePlatform,
                alightStop.ArrivalPlatform));
        }

        if (segments.Count == 0) return null;

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
            segments, departure, arrival, segments.Count - 1, totalDuration, transferTime);
    }

    private static bool OperatesOnDate(PlkRouteDto route, DateOnly date)
    {
        var dates = route.OperatingDates;
        if (dates is null || dates.Count == 0)
            return true;
        return dates.Contains(date);
    }

    private static TimeOnly? TryParse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (TimeOnly.TryParse(raw, out var t)) return t;
        if (TimeSpan.TryParse(raw, out var ts)) return TimeOnly.FromTimeSpan(ts);
        return null;
    }
}

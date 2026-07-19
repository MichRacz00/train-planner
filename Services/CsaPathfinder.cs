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

    // Scan state in the priority queue
    private sealed class ScanState
    {
        public int CurrentStationId { get; }
        public TimeOnly CurrentTime { get; }
        public RouteData Route { get; }
        public int NextStopIndex { get; }
        public int TransfersUsed { get; }
        public List<TripSegment> Segments { get; }
        public TimeOnly SegmentStart { get; }
        public int SegmentStartStation { get; }

        public ScanState(int stationId, TimeOnly time, RouteData route, int nextIndex,
            int transfers, List<TripSegment> segments, TimeOnly segStart, int segStartStation)
        {
            CurrentStationId = stationId;
            CurrentTime = time;
            Route = route;
            NextStopIndex = nextIndex;
            TransfersUsed = transfers;
            Segments = segments;
            SegmentStart = segStart;
            SegmentStartStation = segStartStation;
        }
    }

    public async Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId, int toStationId, DateOnly travelDate, CancellationToken ct = default)
    {
        // Phase 1: Load all route data for the date
        var routeIds = await _tripService.GetAllRouteIdsAsync(travelDate);

        var allRoutes = new List<RouteData>();
        var routesByStation = new Dictionary<int, List<RouteData>>();

        foreach (var id in routeIds)
        {
            ct.ThrowIfCancellationRequested();

            var route = await _tripService.GetRouteDetailsAsync(id.ScheduleId, id.OrderId);
            if (route is null || route.Stations is not { Count: >= 2 })
                continue;

            if (!OperatesOnDate(route, travelDate))
                continue;

            var data = new RouteData(route);
            if (data.Stops.Length < 2)
                continue;

            allRoutes.Add(data);

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



        // Phase 2: CSA scan using priority queue (min-heap on arrival time)
        var pq = new PriorityQueue<ScanState, TimeOnly>();
        var earliestArrival = new Dictionary<int, TimeOnly>();
        var results = new List<MultiSegmentTrip>();

        // Seed: enqueue all departures from origin
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

                    var segments = new List<TripSegment>();
                    var state = new ScanState(
                        fromStationId, depTime, route, i + 1, 0,
                        segments, depTime, fromStationId);
                    pq.Enqueue(state, depTime);
                    break; // First departure stop for this route at origin
                }
            }
        }

        // Phase 3: Scan loop
        while (pq.Count > 0 && results.Count < MaxResults)
        {
            var current = pq.Dequeue();

            // Prune dominated paths
            if (earliestArrival.TryGetValue(current.CurrentStationId, out var best) &&
                current.CurrentTime >= best)
                continue;

            earliestArrival[current.CurrentStationId] = current.CurrentTime;

            // Advance to next stop on current train
            if (current.NextStopIndex < current.Route.Stops.Length)
            {
                var nextStop = current.Route.Stops[current.NextStopIndex];

                if (nextStop.Arrival is { } arrTime)
                {
                    // Build segment from where we boarded to this stop
                    var segment = new TripSegment(
                        current.Route.ScheduleId,
                        current.Route.OrderId,
                        current.Route.TrainName,
                        current.Route.CarrierCode,
                        current.Route.CommercialCategory,
                        new PlkStation(current.SegmentStartStation, "", ""),
                        new PlkStation(nextStop.StationId, "", ""),
                        current.SegmentStart,
                        arrTime,
                        GetDeparturePlatform(current.Route, current.SegmentStartStation),
                        nextStop.ArrivalPlatform);

                    var newSegments = new List<TripSegment>(current.Segments) { segment };

                    if (nextStop.StationId == toStationId)
                    {
                        var trip = AssembleTrip(newSegments);
                        if (trip is not null)
                            results.Add(trip);
                        continue;
                    }

                    // Continue on same train
                    if (current.NextStopIndex + 1 < current.Route.Stops.Length)
                    {
                        var followingStop = current.Route.Stops[current.NextStopIndex + 1];
                        if (followingStop.Departure is { } nextDep)
                        {
                            var contState = new ScanState(
                                nextStop.StationId, arrTime, current.Route,
                                current.NextStopIndex + 1, current.TransfersUsed,
                                newSegments, nextDep, nextStop.StationId);
                            pq.Enqueue(contState, nextDep);
                        }
                    }

                    // Try transfers at this station
                    if (current.TransfersUsed < MaxTransfers &&
                        routesByStation.TryGetValue(nextStop.StationId, out var transferRoutes))
                    {
                        var earliestDeparture = arrTime.Add(MinTransferTime);

                        foreach (var tr in transferRoutes)
                        {
                            if (tr.ScheduleId == current.Route.ScheduleId &&
                                tr.OrderId == current.Route.OrderId)
                                continue;

                            for (var j = 0; j < tr.Stops.Length; j++)
                            {
                                var tStop = tr.Stops[j];
                                if (tStop.StationId != nextStop.StationId) continue;
                                if (tStop.Departure is not { } trDep) continue;
                                if (trDep < earliestDeparture) continue;
                                if (j + 1 >= tr.Stops.Length) continue;

                                var transferState = new ScanState(
                                    nextStop.StationId, trDep, tr, j + 1,
                                    current.TransfersUsed + 1, newSegments,
                                    trDep, nextStop.StationId);
                                pq.Enqueue(transferState, trDep);
                                break;
                            }
                        }
                    }
                }
            }
        }

        return results
            .OrderBy(t => t.TotalDuration)
            .ThenBy(t => t.Transfers)
            .Take(MaxResults)
            .ToArray();
    }

    private static string? GetDeparturePlatform(RouteData route, int stationId)
    {
        var stop = route.Stops.FirstOrDefault(s => s.StationId == stationId);
        return stop?.DeparturePlatform;
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

    private static MultiSegmentTrip? AssembleTrip(List<TripSegment> segments)
    {
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
}

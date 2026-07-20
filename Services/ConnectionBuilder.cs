using Microsoft.Extensions.Logging;
using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Converts PLK route DTOs into sorted Connection objects ready for CSA scanning.
/// Owns all knowledge of the API's time/day format.
/// </summary>
internal static class ConnectionBuilder
{
    internal sealed class Connection
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

    public static List<Connection> Build(
        IEnumerable<PlkRouteDto> routes,
        DateOnly travelDate,
        ILogger logger,
        CancellationToken ct = default)
    {
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

            if (routesProcessed % 50 == 0)
                logger.LogDebug("ConnectionBuilder: {RoutesProcessed} routes processed, {ConnectionCount} connections so far", routesProcessed, connections.Count);

            if (connectionsAdded > 0)
                logger.LogDebug("ConnectionBuilder: route {ScheduleId}/{OrderId} yielded {ConnectionsAdded} connections", route.ScheduleId, route.OrderId, connectionsAdded);
        }

        connections.Sort((a, b) => a.DepartureTime.CompareTo(b.DepartureTime));

        logger.LogInformation("ConnectionBuilder: {RoutesProcessed} routes processed, {RoutesSkipped} skipped, {ConnectionCount} connections built and sorted",
            routesProcessed, routesSkipped, connections.Count);

        return connections;
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

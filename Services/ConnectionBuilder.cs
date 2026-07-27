using Microsoft.Extensions.Logging;
using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Converts PLK route DTOs into sorted TrainLeg objects ready for CSA scanning.
/// Owns all knowledge of the API's time/day format.
/// Times are stored as DateTime using travelDate as the base, so overnight
/// trains (DepartureDay=0, ArrivalDay=1) are represented as consecutive DateTimes
/// and compare correctly without any modulo arithmetic.
/// </summary>
internal static class ConnectionBuilder
{
    public static List<TrainLeg> Build(
        IEnumerable<PlkRouteDto> routes,
        DateOnly travelDate,
        ILogger logger,
        CancellationToken ct = default)
    {
        var legs = new List<TrainLeg>();
        var routesProcessed = 0;
        var routesSkipped = 0;
        var baseDate = travelDate.ToDateTime(TimeOnly.MinValue);

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
            var legsAdded = 0;

            for (var i = 0; i < route.Stations.Count - 1; i++)
            {
                var from = route.Stations[i];
                var to   = route.Stations[i + 1];

                if (from.DepartureTime is null || to.ArrivalTime is null)
                    continue;

                if (!TryParseTime(from.DepartureTime, out var depTime))
                    continue;
                if (!TryParseTime(to.ArrivalTime, out var arrTime))
                    continue;

                var depDay = from.DepartureDay ?? 0;
                var arrDay = to.ArrivalDay ?? 0;
                var dep = baseDate + depTime.ToTimeSpan() + TimeSpan.FromHours(24 * depDay);
                var arr = baseDate + arrTime.ToTimeSpan() + TimeSpan.FromHours(24 * arrDay);

                legs.Add(new TrainLeg
                {
                    FromStationId     = from.StationId,
                    ToStationId       = to.StationId,
                    Departure         = dep,
                    Arrival           = arr,
                    ScheduleId        = route.ScheduleId,
                    OrderId           = route.OrderId,
                    TrainName         = route.Name ?? route.NationalNumber ?? $"{route.CarrierCode} {route.OrderId}",
                    CarrierCode       = route.CarrierCode ?? "",
                    CommercialCategory = route.CommercialCategorySymbol ?? "",
                    DeparturePlatform = from.DeparturePlatform,
                    ArrivalPlatform   = to.ArrivalPlatform
                });
                legsAdded++;
            }

            if (routesProcessed % 50 == 0)
                logger.LogDebug("ConnectionBuilder: {RoutesProcessed} routes processed, {LegCount} legs so far", routesProcessed, legs.Count);

            if (legsAdded > 0)
                logger.LogDebug("ConnectionBuilder: route {ScheduleId}/{OrderId} yielded {LegsAdded} legs", route.ScheduleId, route.OrderId, legsAdded);
        }

        legs.Sort((a, b) => a.Departure.CompareTo(b.Departure));

        logger.LogInformation("ConnectionBuilder: {RoutesProcessed} routes processed, {RoutesSkipped} skipped, {LegCount} legs built and sorted",
            routesProcessed, routesSkipped, legs.Count);

        return legs;
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

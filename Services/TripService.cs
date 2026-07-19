using System.Text.Json;
using System.Xml;
using train_planner.Models;

namespace train_planner.Services;

public class TripService(IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Hardcoded list of major PKP PLK stations (Code = PLK station integer ID).
    // Stations[0] and Stations[1] are the default From/To fallback — keep them a real intercity pair.
    private static readonly List<Station> Stations =
    [
        new("33605", "Warszawa Centralna", "Warszawa"),
        new("80416", "Kraków Główny",      "Kraków"),
        new("33512", "Warszawa Wschodnia", "Warszawa"),
        new("31445", "Gdańsk Główny",      "Gdańsk"),
        new("29228", "Wrocław Główny",     "Wrocław"),
        new("25139", "Poznań Główny",      "Poznań"),
        new("29032", "Łódź Fabryczna",     "Łódź"),
        new("36145", "Katowice",           "Katowice"),
        new("35045", "Lublin",             "Lublin"),
        new("24904", "Rzeszów Główny",     "Rzeszów"),
    ];

    public IReadOnlyList<Station> GetStations() => Stations.AsReadOnly();

    public async Task<List<TripResult>> SearchTripsAsync(TripSearchParams searchParams)
    {
        var from = Stations.FirstOrDefault(s => s.Code == searchParams.FromStation) ?? Stations[0];
        var to   = Stations.FirstOrDefault(s => s.Code == searchParams.ToStation)   ?? Stations[1];

        var fromId = from.Code;
        var toId   = to.Code;
        var date   = searchParams.TravelDate.ToString("yyyy-MM-dd");

        var client = httpClientFactory.CreateClient("PlkApi");

        // 1. Schedules — directional filter with integer station IDs
        var scheduleUrl = $"/api/v1/schedules?dateFrom={date}&dateTo={date}&fromStations={fromId}&toStations={toId}";
        var scheduleRaw = await client.GetStringAsync(scheduleUrl);
        var scheduleResp = JsonSerializer.Deserialize<PlkScheduleResponse>(scheduleRaw, JsonOptions);

        if (scheduleResp?.Routes is not { Count: > 0 } routes)
            return [];

        // 2. Real-time operations
        var opsUrl = $"/api/v1/operations?stations={fromId},{toId}&withPlanned=true";
        var opsRaw = await client.GetStringAsync(opsUrl);
        var opsResp = JsonSerializer.Deserialize<PlkOperationResponse>(opsRaw, JsonOptions);

        var opsLookup = (opsResp?.Trains ?? [])
            .GroupBy(t => (t.ScheduleId, t.OrderId))
            .ToDictionary(g => g.Key, g => g.First());

        var fromIdInt = int.Parse(fromId);
        var toIdInt   = int.Parse(toId);
        var results   = new List<TripResult>();

        foreach (var route in routes)
        {
            var fromStop = route.Stations?.FirstOrDefault(s => s.StationId == fromIdInt);
            var toStop   = route.Stations?.FirstOrDefault(s => s.StationId == toIdInt);

            if (fromStop is null || toStop is null) continue;
            if (fromStop.OrderNumber >= toStop.OrderNumber) continue;

            if (!TryParseDuration(fromStop.DepartureTime, out var depOffset)) continue;
            if (!TryParseDuration(toStop.ArrivalTime,     out var arrOffset)) continue;

            var category = route.CommercialCategorySymbol ?? "";
            var trainType = category switch
            {
                "EIP" or "EIC" => TrainType.HighSpeed,
                "IC"  or "TLK" => TrainType.InterCity,
                "NJ"  or "EN"  => TrainType.Night,
                _              => TrainType.Regional,
            };

            var trainName = route.Name ?? route.NationalNumber ?? $"{route.CarrierCode} {route.OrderId}";

            string? delayInfo = null;
            if (opsLookup.TryGetValue((route.ScheduleId, route.OrderId), out var op))
            {
                var opFrom = op.Stations?.FirstOrDefault(s => s.StationId == fromIdInt);
                if (opFrom?.DepartureDelayMinutes is { } delay && delay > 0)
                    delayInfo = $"+{delay}min";
            }

            var amenities = new List<string>();
            if (!string.IsNullOrEmpty(delayInfo)) amenities.Add(delayInfo);

            results.Add(new TripResult
            {
                TripId         = $"{route.ScheduleId}-{route.OrderId}",
                From           = from,
                To             = to,
                Departure      = TimeOnly.FromTimeSpan(depOffset),
                Arrival        = TimeOnly.FromTimeSpan(arrOffset),
                PricePerPerson = 0m,
                Segments       = [new(trainType, trainName, fromStop.DeparturePlatform ?? "")],
                Amenities      = amenities,
            });
        }

        return results.OrderBy(r => r.Departure).ToList();
    }

    private static bool TryParseDuration(string? raw, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        if (string.IsNullOrEmpty(raw)) return false;
        // Plain HH:mm:ss or HH:mm
        if (TimeSpan.TryParse(raw, out ts)) return true;
        // ISO 8601 duration e.g. PT14H30M
        try { ts = XmlConvert.ToTimeSpan(raw); return true; }
        catch { return false; }
    }
}

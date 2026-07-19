using System.Text.Json;
using train_planner.Models;

namespace train_planner.Services;

public class PlkTripService(IHttpClientFactory httpClientFactory) : IPlkTripService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Station list changes rarely — cache for the lifetime of the app process.
    private static IReadOnlyList<PlkStation>? _stationsCache;
    private static readonly SemaphoreSlim StationsSemaphore = new(1, 1);

    public async Task<IReadOnlyList<PlkStation>> GetStationsAsync(
        string? search = null, int pageSize = 200)
    {
        // Searched calls bypass the cache entirely
        if (string.IsNullOrEmpty(search) && _stationsCache is not null)
            return _stationsCache;

        await StationsSemaphore.WaitAsync();
        try
        {
            // Double-check inside the lock — another circuit may have populated it
            if (string.IsNullOrEmpty(search) && _stationsCache is not null)
                return _stationsCache;

            var result = await FetchAllStationsAsync(search, pageSize);

            if (string.IsNullOrEmpty(search))
                _stationsCache = result;

            return result;
        }
        finally
        {
            StationsSemaphore.Release();
        }
    }

    private async Task<IReadOnlyList<PlkStation>> FetchAllStationsAsync(
        string? search, int pageSize)
    {
        var client = CreateClient();
        var searchSuffix = string.IsNullOrEmpty(search) ? "" : $"&search={Uri.EscapeDataString(search)}";
        var all = new List<PlkStation>();
        var page = 1;
        int totalPages;

        do
        {
            var url = $"/api/v1/dictionaries/stations?page={page}&pageSize={pageSize}{searchSuffix}";
            var raw = await client.GetStringAsync(url);
            var response = JsonSerializer.Deserialize<PlkStationsResponse>(raw, JsonOptions);

            if (response?.Stations is { Count: > 0 } stations)
                all.AddRange(stations.Select(s => new PlkStation(s.Id, s.Name ?? "", "")));

            totalPages = response?.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);

        return all;
    }

    public async Task<List<ScheduledTrip>> SearchTripsAsync(PlkTripSearchParams searchParams)
    {
        var client = CreateClient();
        var date = searchParams.TravelDate.ToString("yyyy-MM-dd");

        var stationsParam = $"{searchParams.FromStationId},{searchParams.ToStationId}";

        // 1. Fetch scheduled routes for the date — directional filter
        var scheduleUrl = $"/api/v1/schedules?dateFrom={date}&dateTo={date}"
                        + $"&fromStations={searchParams.FromStationId}&toStations={searchParams.ToStationId}";
        var scheduleRaw = await client.GetStringAsync(scheduleUrl);
        var scheduleResp = JsonSerializer.Deserialize<PlkScheduleResponse>(scheduleRaw);

        if (scheduleResp?.Routes is not { Count: > 0 } routes)
            return [];

        // 2. Fetch real-time operations for same stations
        var opsUrl = $"/api/v1/operations?stations={searchParams.FromStationId},{searchParams.ToStationId}&withPlanned=true";
        var opsRaw = await client.GetStringAsync(opsUrl);
        var opsResp = JsonSerializer.Deserialize<PlkOperationResponse>(opsRaw);

        // GroupBy handles duplicate (ScheduleId, OrderId) pairs the API may return
        var opsLookup = (opsResp?.Trains ?? [])
            .GroupBy(t => (t.ScheduleId, t.OrderId))
            .ToDictionary(g => g.Key, g => g.First());

        var stationNames = scheduleResp.Dictionaries?.Stations
                           ?? new Dictionary<string, PlkStationDictionaryDto>();

        var fromStation = new PlkStation(searchParams.FromStationId,
                                         LookupStationName(stationNames, searchParams.FromStationId), "");
        var toStation = new PlkStation(searchParams.ToStationId,
                                       LookupStationName(stationNames, searchParams.ToStationId), "");

        var results = new List<ScheduledTrip>();

        foreach (var route in routes)
        {
            var fromStop = route.Stations?
                .FirstOrDefault(s => s.StationId == searchParams.FromStationId);
            var toStop = route.Stations?
                .FirstOrDefault(s => s.StationId == searchParams.ToStationId);

            if (fromStop is null || toStop is null) continue;
            if (fromStop.OrderNumber >= toStop.OrderNumber) continue;

            if (!TryParseDuration(fromStop.DepartureTime, out var depOffset)) continue;
            if (!TryParseDuration(toStop.ArrivalTime, out var arrOffset)) continue;

            var plannedDep = TimeOnly.FromTimeSpan(depOffset);
            var plannedArr = TimeOnly.FromTimeSpan(arrOffset);

            TimeOnly? actualDep = null, actualArr = null;
            int? depDelay = null, arrDelay = null;
            string? trainStatus = null;

            if (opsLookup.TryGetValue((route.ScheduleId, route.OrderId), out var op))
            {
                trainStatus = op.TrainStatus;
                var opFrom = op.Stations?.FirstOrDefault(s => s.StationId == searchParams.FromStationId);
                var opTo = op.Stations?.FirstOrDefault(s => s.StationId == searchParams.ToStationId);

                if (opFrom?.ActualDeparture is { } ad)
                    actualDep = TimeOnly.FromDateTime(ad);
                if (opTo?.ActualArrival is { } aa)
                    actualArr = TimeOnly.FromDateTime(aa);
                depDelay = opFrom?.DepartureDelayMinutes;
                arrDelay = opTo?.ArrivalDelayMinutes;
            }

            results.Add(new ScheduledTrip
            {
                ScheduleId = route.ScheduleId,
                OrderId = route.OrderId,
                TrainName = route.Name ?? route.NationalNumber ?? $"{route.CarrierCode} {route.OrderId}",
                CarrierCode = route.CarrierCode ?? "",
                CommercialCategory = route.CommercialCategorySymbol ?? "",
                From = fromStation,
                To = toStation,
                PlannedDeparture = plannedDep,
                PlannedArrival = plannedArr,
                DeparturePlatform = fromStop.DeparturePlatform,
                ActualDeparture = actualDep,
                ActualArrival = actualArr,
                DepartureDelayMinutes = depDelay,
                ArrivalDelayMinutes = arrDelay,
                TrainStatus = trainStatus,
            });
        }

        return results.OrderBy(t => t.PlannedDeparture).ToList();
    }

    private HttpClient CreateClient()
        => httpClientFactory.CreateClient("PlkApi");

    private static string LookupStationName(
        Dictionary<string, PlkStationDictionaryDto> dict, int id)
        => dict.TryGetValue(id.ToString(), out var s) ? s.Name ?? id.ToString() : id.ToString();

    private static bool TryParseDuration(string? raw, out TimeSpan ts)
    {
        ts = TimeSpan.Zero;
        if (string.IsNullOrEmpty(raw)) return false;
        return TimeSpan.TryParse(raw, out ts);
    }
}

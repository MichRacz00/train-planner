using System.Net.Http.Json;
using System.Xml;
using Microsoft.Extensions.Options;
using train_planner.Models;

namespace train_planner.Services;

public class PlkTripService(IHttpClientFactory httpClientFactory) : IPlkTripService
{
    public async Task<IReadOnlyList<PlkStation>> GetStationsAsync(
        string? search = null, int pageSize = 500)
    {
        var client = CreateClient();
        var url = $"/api/v1/dictionaries/stations?pageSize={pageSize}"
                + (string.IsNullOrEmpty(search) ? "" : $"&search={Uri.EscapeDataString(search)}");

        var response = await client.GetFromJsonAsync<PlkStationsResponse>(url);
        return response?.Stations?
                   .Select(s => new PlkStation(s.Id, s.Name ?? "", ""))
                   .ToList()
               ?? [];
    }

    public async Task<List<ScheduledTrip>> SearchTripsAsync(PlkTripSearchParams searchParams)
    {
        var client = CreateClient();
        var date = searchParams.TravelDate.ToString("yyyy-MM-dd");

        var stationsParam = $"{searchParams.FromStationId},{searchParams.ToStationId}";

        // 1. Fetch scheduled routes for the date
        var scheduleUrl = $"/api/v1/schedules?dateFrom={date}&dateTo={date}"
                        + $"&stations={stationsParam}";
        var scheduleResp = await client.GetFromJsonAsync<PlkScheduleResponse>(scheduleUrl);

        if (scheduleResp?.Routes is not { Count: > 0 } routes)
            return [];

        // 2. Fetch real-time operations for same stations
        var opsUrl = $"/api/v1/operations?stations={stationsParam}&withPlanned=true";
        var opsResp = await client.GetFromJsonAsync<PlkOperationResponse>(opsUrl);

        var opsLookup = (opsResp?.Trains ?? [])
            .ToDictionary(t => (t.ScheduleId, t.OrderId));

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
        try { ts = XmlConvert.ToTimeSpan(raw); return true; }
        catch { return false; }
    }
}

namespace TrainPlanner.Models;

// ── PLK API data models ─────────────────────────────────────────────────────

public record PlkStation(int Id, string Name)
{
    public string Code => Id.ToString();
}

public record PlkTripSearchParams
{
    public int FromStationId { get; set; }
    public int ToStationId { get; set; }
    public DateOnly TravelDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public TimeOnly DepartureTime { get; set; } = TimeOnly.FromTimeSpan(TimeSpan.Zero);
}

public record ScheduledTrip
{
    public required int ScheduleId { get; init; }
    public required int OrderId { get; init; }
    public required string TrainName { get; init; }
    public required string CarrierCode { get; init; }
    public required string CommercialCategory { get; init; }
    public required PlkStation From { get; init; }
    public required PlkStation To { get; init; }
    public required TimeOnly PlannedDeparture { get; init; }
    public required TimeOnly PlannedArrival { get; init; }
    public string? DeparturePlatform { get; init; }

    public TimeOnly? ActualDeparture { get; init; }
    public TimeOnly? ActualArrival { get; init; }
    public int? DepartureDelayMinutes { get; init; }
    public int? ArrivalDelayMinutes { get; init; }
    public string? TrainStatus { get; init; }

    public TimeSpan PlannedDuration =>
        PlannedArrival > PlannedDeparture
            ? PlannedArrival - PlannedDeparture
            : TimeSpan.FromHours(24) - (PlannedDeparture - PlannedArrival);

    public string FormattedDuration
    {
        get
        {
            var d = PlannedDuration;
            return d.Hours > 0
                ? $"{d.Hours}h {d.Minutes:D2}m"
                : $"{d.Minutes}m";
        }
    }

}

// A single leg of a multi-segment journey (also used as the CSA graph edge)
public sealed record JourneySegment
{
    public int FromStationId { get; init; }
    public int ToStationId { get; init; }
    public DateTime Departure { get; init; }
    public DateTime Arrival { get; init; }
    public int ScheduleId { get; init; }
    public int OrderId { get; init; }
    public string TrainName { get; init; } = "";
    public string CarrierCode { get; init; } = "";
    public string CommercialCategory { get; init; } = "";
    public string? DeparturePlatform { get; init; }
    public string? ArrivalPlatform { get; init; }
    
    public TimeOnly DepartureTime => TimeOnly.FromDateTime(Departure);
    public TimeOnly ArrivalTime   => TimeOnly.FromDateTime(Arrival);
    
    public override string ToString() =>
        $"{TrainName} {FromStationId}->{ToStationId} {DepartureTime:HH:mm}-{ArrivalTime:HH:mm}";
}

// A complete multi-segment journey produced by the CSA pathfinder
public record Journey(
    IReadOnlyList<JourneySegment> Legs,
    int Transfers,
    TimeSpan TotalDuration)
{
    public DateTime Departure          => Legs[0].Departure;
    public DateTime Arrival            => Legs[^1].Arrival;
    public TimeOnly DepartureTimeOfDay => TimeOnly.FromDateTime(Departure);
    public TimeOnly ArrivalTimeOfDay   => TimeOnly.FromDateTime(Arrival);
    
    public override string ToString()
    {
        var parts = new List<string>();
        var i = 0;
        while (i < Legs.Count)
        {
            var first = Legs[i];
            var j = i + 1;
            while (j < Legs.Count &&
                   Legs[j].ScheduleId == first.ScheduleId &&
                   Legs[j].OrderId    == first.OrderId)
                j++;
            var last = Legs[j - 1];
            var dep  = TimeOnly.FromDateTime(first.Departure);
            var arr  = TimeOnly.FromDateTime(last.Arrival);
            parts.Add($"{first.TrainName} {first.FromStationId}->{last.ToStationId} {dep:HH:mm}-{arr:HH:mm}");
            i = j;
        }
        return string.Join(" | ", parts);
    }
}

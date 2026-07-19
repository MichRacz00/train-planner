namespace train_planner.Models;

public record Station(string Code, string Name, string City);


public enum TrainType
{
    Regional,
    InterCity,
    HighSpeed,
    Night
}

public record JourneySegment(TrainType TrainType, string TrainNumber, string Platform);

public record TripResult
{
    public required string TripId { get; init; }
    public required Station From { get; init; }
    public required Station To { get; init; }
    public required TimeOnly Departure { get; init; }
    public required TimeOnly Arrival { get; init; }
    public required decimal PricePerPerson { get; init; }
    public List<JourneySegment> Segments { get; init; } = [];
    public List<string> Amenities { get; init; } = [];

    public int Transfers => Math.Max(0, Segments.Count - 1);

    public TimeSpan Duration => Arrival > Departure
        ? Arrival - Departure
        : TimeSpan.FromHours(24) - (Departure - Arrival);

    public string FormattedDuration
    {
        get
        {
            var d = Duration;
            return d.Hours > 0
                ? $"{d.Hours}h {d.Minutes:D2}m"
                : $"{d.Minutes}m";
        }
    }
}

// ── PLK API data models ─────────────────────────────────────────────────────

public record PlkStation(int Id, string Name, string City)
{
    // Compat: markup uses station.Code (string) for select option values
    public string Code => Id.ToString();
}

public record PlkTripSearchParams
{
    public int FromStationId { get; set; }
    public int ToStationId { get; set; }
    public DateOnly TravelDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
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

    // Compat aliases — keep Home.razor markup compilable without markup changes
    public TimeOnly Departure      => PlannedDeparture;
    public TimeOnly Arrival        => PlannedArrival;
    public decimal  PricePerPerson => 0m;
    public IReadOnlyList<JourneySegment> Segments =>
        CommercialCategory is { Length: > 0 } cat
            ? [new JourneySegment(
                  cat switch
                  {
                      "EIP" or "EIC" => TrainType.HighSpeed,
                      "IC"  or "TLK" => TrainType.InterCity,
                      "NJ"  or "EN"  => TrainType.Night,
                      _              => TrainType.Regional,
                  },
                  TrainName,
                  DeparturePlatform ?? "")]
            : [];
}

// A single leg of a multi-segment journey
public record TripSegment(
    int ScheduleId,
    int OrderId,
    string TrainName,
    string CarrierCode,
    string CommercialCategory,
    PlkStation From,
    PlkStation To,
    TimeOnly PlannedDeparture,
    TimeOnly PlannedArrival,
    string? DeparturePlatform,
    string? ArrivalPlatform);

// A complete multi-segment journey produced by the CSA pathfinder
public record MultiSegmentTrip(
    IReadOnlyList<TripSegment> Segments,
    TimeOnly DepartureTime,
    TimeOnly ArrivalTime,
    int Transfers,
    TimeSpan TotalDuration,
    TimeSpan TransferTime);

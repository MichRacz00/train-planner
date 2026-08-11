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
    private string _trainName = "";
    public string TrainName
    {
        get => _trainName;
        init
        {
            if (value.All(char.IsDigit)) { _trainName = ""; return; }
            _trainName = string.Join(' ', value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpper(w[0]) + w[1..].ToLowerInvariant()));
        }
    }
    public string CarrierCode { get; init; } = "";
    private string _rawCategory = "";
    private TrainCategory _category = TrainCategories.Resolve(null);
    public string CommercialCategory
    {
        get => _rawCategory;
        init { _rawCategory = value; _category = TrainCategories.Resolve(value); }
    }
    public TrainCategory Category => _category;
    public string? DeparturePlatform { get; init; }
    public string? ArrivalPlatform { get; init; }

    public TimeOnly DepartureTime => TimeOnly.FromDateTime(Departure);
    public TimeOnly ArrivalTime   => TimeOnly.FromDateTime(Arrival);

    public string GetFullTrainName() =>
        string.IsNullOrEmpty(_trainName)
            ? Category.DisplayName
            : $"{Category.DisplayName} {_trainName}";

    public override string ToString() =>
        $"[{FromStationId}] {DepartureTime:HH:mm} -{GetFullTrainName()}-> {ArrivalTime:HH:mm} [{ToStationId}]";
}

// A complete multi-segment journey produced by the CSA pathfinder
public record Journey(
    IReadOnlyList<JourneySegment> Segments,
    int Transfers,
    TimeSpan TotalDuration)
{
    public DateTime Departure          => Segments[0].Departure;
    public DateTime Arrival            => Segments[^1].Arrival;
    public TimeOnly DepartureTimeOfDay => TimeOnly.FromDateTime(Departure);
    public TimeOnly ArrivalTimeOfDay   => TimeOnly.FromDateTime(Arrival);
    
    public IReadOnlyList<JourneySegment> Legs { get; } = BuildJourneyLegs(Segments);

    private static IReadOnlyList<JourneySegment> BuildJourneyLegs(IReadOnlyList<JourneySegment> legs)
    {
        var result = new List<JourneySegment>();
        var i = 0;
        while (i < legs.Count)
        {
            var first = legs[i];
            var j = i + 1;
            while (j < legs.Count &&
                   legs[j].ScheduleId == first.ScheduleId &&
                   legs[j].OrderId    == first.OrderId)
                j++;
            var last = legs[j - 1];
            result.Add(first with { ToStationId = last.ToStationId, Arrival = last.Arrival });
            i = j;
        }
        return result;
    }

    public override string ToString()
    {
        if (Legs.Count == 0)
            return string.Empty;

        var parts = new List<string>
        {
            $"[{Legs[0].FromStationId.ToString()}]"
        };

        foreach (var leg in Legs)
        {
            parts.Add(
                $"{leg.Departure:HH:mm} -{leg.GetFullTrainName()}-> {leg.Arrival:HH:mm}"
            );

            parts.Add($"[{leg.ToStationId.ToString()}]");
        }

        return string.Join(" ", parts);
    }
}

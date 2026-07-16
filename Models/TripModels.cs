namespace train_planner.Models;

public record Station(string Code, string Name, string City);

public record TripSearchParams
{
    public string FromStation { get; set; } = string.Empty;
    public string ToStation { get; set; } = string.Empty;
    public DateOnly TravelDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Passengers { get; set; } = 1;
}

public enum TrainType
{
    Regional,
    InterCity,
    HighSpeed,
    Night
}

/// <summary>One leg of a journey: a single train service from one stop to the next.</summary>
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

    // Derived convenience properties
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

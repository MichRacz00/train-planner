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

public record TripResult
{
    public required string TripId { get; init; }
    public required Station From { get; init; }
    public required Station To { get; init; }
    public required TimeOnly Departure { get; init; }
    public required TimeOnly Arrival { get; init; }
    public required TrainType TrainType { get; init; }
    public required string TrainNumber { get; init; }
    public required decimal PricePerPerson { get; init; }
    public int Transfers { get; init; } = 0;
    public List<string> Amenities { get; init; } = [];

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

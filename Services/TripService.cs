using train_planner.Models;

namespace train_planner.Services;

public class TripService
{
    private static readonly List<Station> Stations =
    [
        new("CPH", "Copenhagen Central", "Copenhagen"),
        new("AAR", "Aarhus H", "Aarhus"),
        new("ODE", "Odense", "Odense"),
        new("AAL", "Aalborg", "Aalborg"),
        new("ESB", "Esbjerg", "Esbjerg"),
        new("RKE", "Roskilde", "Roskilde"),
        new("HEL", "Helsingør", "Helsingør"),
        new("KOL", "Kolding", "Kolding"),
        new("VEJ", "Vejle", "Vejle"),
        new("HOR", "Horsens", "Horsens"),
    ];

    public IReadOnlyList<Station> GetStations() => Stations.AsReadOnly();

    public Task<List<TripResult>> SearchTripsAsync(TripSearchParams searchParams)
    {
        var from = Stations.FirstOrDefault(s => s.Code == searchParams.FromStation) ?? Stations[0];
        var to   = Stations.FirstOrDefault(s => s.Code == searchParams.ToStation)   ?? Stations[1];

        var results = new List<TripResult>
        {
            // Direct — single segment
            new()
            {
                TripId = "IC-101",
                From = from,
                To = to,
                Departure = new TimeOnly(6, 32),
                Arrival = new TimeOnly(9, 15),
                PricePerPerson = 199m,
                Segments =
                [
                    new(TrainType.InterCity, "IC 101", "3")
                ],
                Amenities = ["WiFi", "Bistro", "Power outlets"]
            },
            // 1 transfer — two segments
            new()
            {
                TripId = "RE-204",
                From = from,
                To = to,
                Departure = new TimeOnly(7, 48),
                Arrival = new TimeOnly(11, 03),
                PricePerPerson = 149m,
                Segments =
                [
                    new(TrainType.Regional,   "RE 204", "5"),
                    new(TrainType.InterCity,  "IC 812", "2")
                ],
                Amenities = ["Power outlets"]
            },
            // Direct — high speed
            new()
            {
                TripId = "HS-007",
                From = from,
                To = to,
                Departure = new TimeOnly(9, 00),
                Arrival = new TimeOnly(10, 45),
                PricePerPerson = 289m,
                Segments =
                [
                    new(TrainType.HighSpeed, "HS 7", "1")
                ],
                Amenities = ["WiFi", "Bistro", "First class", "Power outlets", "Quiet zone"]
            },
            // Direct — intercity
            new()
            {
                TripId = "IC-305",
                From = from,
                To = to,
                Departure = new TimeOnly(11, 15),
                Arrival = new TimeOnly(14, 02),
                PricePerPerson = 199m,
                Segments =
                [
                    new(TrainType.InterCity, "IC 305", "4")
                ],
                Amenities = ["WiFi", "Bistro", "Power outlets"]
            },
            // 2 transfers — three segments
            new()
            {
                TripId = "RE-410",
                From = from,
                To = to,
                Departure = new TimeOnly(13, 22),
                Arrival = new TimeOnly(17, 40),
                PricePerPerson = 129m,
                Segments =
                [
                    new(TrainType.Regional,  "RE 410", "6"),
                    new(TrainType.Regional,  "RE 521", "3"),
                    new(TrainType.InterCity, "IC 630", "1")
                ],
                Amenities = []
            },
            // Direct — intercity
            new()
            {
                TripId = "IC-507",
                From = from,
                To = to,
                Departure = new TimeOnly(16, 48),
                Arrival = new TimeOnly(19, 30),
                PricePerPerson = 219m,
                Segments =
                [
                    new(TrainType.InterCity, "IC 507", "2")
                ],
                Amenities = ["WiFi", "Power outlets"]
            },
        };

        return Task.FromResult(results);
    }
}

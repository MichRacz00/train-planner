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
        // Stub: return hardcoded results regardless of search params
        var from = Stations.FirstOrDefault(s => s.Code == searchParams.FromStation)
                   ?? Stations[0];
        var to = Stations.FirstOrDefault(s => s.Code == searchParams.ToStation)
                 ?? Stations[1];

        var results = new List<TripResult>
        {
            new()
            {
                TripId = "IC-101",
                From = from,
                To = to,
                Departure = new TimeOnly(6, 32),
                Arrival = new TimeOnly(9, 15),
                TrainType = TrainType.InterCity,
                TrainNumber = "IC 101",
                PricePerPerson = 199m,
                Transfers = 0,
                Amenities = ["WiFi", "Bistro", "Power outlets"]
            },
            new()
            {
                TripId = "RE-204",
                From = from,
                To = to,
                Departure = new TimeOnly(7, 48),
                Arrival = new TimeOnly(11, 03),
                TrainType = TrainType.Regional,
                TrainNumber = "RE 204",
                PricePerPerson = 149m,
                Transfers = 1,
                Amenities = ["Power outlets"]
            },
            new()
            {
                TripId = "HS-007",
                From = from,
                To = to,
                Departure = new TimeOnly(9, 00),
                Arrival = new TimeOnly(10, 45),
                TrainType = TrainType.HighSpeed,
                TrainNumber = "HS 7",
                PricePerPerson = 289m,
                Transfers = 0,
                Amenities = ["WiFi", "Bistro", "First class", "Power outlets", "Quiet zone"]
            },
            new()
            {
                TripId = "IC-305",
                From = from,
                To = to,
                Departure = new TimeOnly(11, 15),
                Arrival = new TimeOnly(14, 02),
                TrainType = TrainType.InterCity,
                TrainNumber = "IC 305",
                PricePerPerson = 199m,
                Transfers = 0,
                Amenities = ["WiFi", "Bistro", "Power outlets"]
            },
            new()
            {
                TripId = "RE-410",
                From = from,
                To = to,
                Departure = new TimeOnly(13, 22),
                Arrival = new TimeOnly(17, 40),
                TrainType = TrainType.Regional,
                TrainNumber = "RE 410",
                PricePerPerson = 129m,
                Transfers = 2,
                Amenities = []
            },
            new()
            {
                TripId = "IC-507",
                From = from,
                To = to,
                Departure = new TimeOnly(16, 48),
                Arrival = new TimeOnly(19, 30),
                TrainType = TrainType.InterCity,
                TrainNumber = "IC 507",
                PricePerPerson = 219m,
                Transfers = 0,
                Amenities = ["WiFi", "Power outlets"]
            },
        };

        // Simulate async work
        return Task.FromResult(results);
    }
}

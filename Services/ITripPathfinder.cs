using TrainPlanner.Models;

namespace TrainPlanner.Services;

public interface ITripPathfinder
{
    Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId,
        int toStationId,
        DateOnly travelDate,
        TimeOnly departureAfter = default,
        CancellationToken ct = default);
}

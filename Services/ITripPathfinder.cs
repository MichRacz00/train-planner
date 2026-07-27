using TrainPlanner.Models;

namespace TrainPlanner.Services;

public interface ITripPathfinder
{
    Task<IReadOnlyList<Journey>> FindTripsAsync(
        int fromStationId,
        int toStationId,
        DateOnly travelDate,
        TimeOnly departureAfter = default,
        CancellationToken ct = default);
}

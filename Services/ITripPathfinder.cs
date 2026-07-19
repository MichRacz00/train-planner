using train_planner.Models;

namespace train_planner.Services;

public interface ITripPathfinder
{
    Task<IReadOnlyList<MultiSegmentTrip>> FindTripsAsync(
        int fromStationId,
        int toStationId,
        DateOnly travelDate,
        CancellationToken ct = default);
}

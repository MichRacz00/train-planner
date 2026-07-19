using train_planner.Models;

namespace train_planner.Services;

public interface IPlkTripService
{
    Task<IReadOnlyList<PlkStation>> GetStationsAsync(string? search = null, int pageSize = 500);
    Task<List<ScheduledTrip>> SearchTripsAsync(PlkTripSearchParams searchParams);
    Task<List<PlkRouteDto>> GetAllRoutesAsync(DateOnly date);
}

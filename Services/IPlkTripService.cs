using TrainPlanner.Models;

namespace TrainPlanner.Services;

public interface IPlkTripService
{
    Task<IReadOnlyList<PlkStation>> GetStationsAsync(string? search = null, int pageSize = 500);
    Task<List<ScheduledTrip>> SearchTripsAsync(PlkTripSearchParams searchParams);
    Task<List<PlkRouteDto>> GetAllRoutesAsync(DateOnly date);
}

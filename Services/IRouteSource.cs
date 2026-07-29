using TrainPlanner.Models;

namespace TrainPlanner.Services;

public interface IRouteSource
{
    Task<List<PlkRouteDto>> GetRoutesAsync(DateOnly date, CancellationToken ct = default);
}

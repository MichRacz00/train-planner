using TrainPlanner.Models;
using TrainPlanner.Services;

namespace TrainPlanner.Tests.Fixtures;

/// <summary>
/// IRouteSource implementation backed by a pre-loaded fixture file.
/// Allows CsaPathfinder to be constructed and exercised in tests without
/// any HTTP calls or DI infrastructure.
/// </summary>
internal sealed class FixtureRouteSource(string fileName) : IRouteSource
{
    private readonly List<PlkRouteDto> _routes = PlkFixture.LoadRoutes(fileName);

    public Task<List<PlkRouteDto>> GetRoutesAsync(DateOnly date, CancellationToken ct = default)
        => Task.FromResult(_routes);
}

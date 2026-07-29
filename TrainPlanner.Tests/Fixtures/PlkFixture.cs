using System.Text.Json;
using Microsoft.Extensions.Logging;
using TrainPlanner.Models;
using TrainPlanner.Services;

namespace TrainPlanner.Tests.Fixtures;

/// <summary>
/// Loads a saved PLK schedule fixture, runs it through ConnectionBuilder,
/// and returns a sorted JourneySegment list ready for CsaAlgorithm.FindJourneys.
/// </summary>
internal static class PlkFixture
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Loads fixture from Fixtures/<paramref name="fileName"/> (relative to the
    /// test assembly output directory) and builds CSA graph edges for the given date.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the fixture file does not exist.
    /// Run: dotnet run --project TrainPlanner.FixtureCapture -- &lt;date&gt;
    /// </exception>
    /// <summary>
    /// Returns the raw <see cref="PlkRouteDto"/> list from the fixture file,
    /// without running ConnectionBuilder. Useful for injecting into
    /// <see cref="FixtureRouteSource"/>.
    /// </summary>
    public static List<PlkRouteDto> LoadRoutes(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fixture not found at '{path}'. " +
                $"Run: dotnet run --project TrainPlanner.FixtureCapture -- <date>",
                path);

        var raw  = File.ReadAllText(path);
        var resp = JsonSerializer.Deserialize<PlkScheduleResponse>(raw, JsonOpts)
                   ?? throw new InvalidDataException($"Failed to deserialize fixture: {path}");

        return resp.Routes ?? [];
    }

    public static List<JourneySegment> LoadLegs(string fileName, DateOnly date)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fixture not found at '{path}'. " +
                $"Run: dotnet run --project TrainPlanner.FixtureCapture -- {date:yyyy-MM-dd}",
                path);

        var raw  = File.ReadAllText(path);
        var resp = JsonSerializer.Deserialize<PlkScheduleResponse>(raw, JsonOpts)
                   ?? throw new InvalidDataException($"Failed to deserialize fixture: {path}");

        var routes = resp.Routes ?? [];

        // Suppress ConnectionBuilder's Info/Debug noise in test output
        using var logFactory = LoggerFactory.Create(b =>
            b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var logger = logFactory.CreateLogger("PlkFixture");

        return ConnectionBuilder.Build(routes, date, logger);
    }
}

using TrainPlanner.Models;
using TrainPlanner.Services;
using TrainPlanner.Tests.Fixtures;
using Xunit;

namespace TrainPlanner.Tests;

/// <summary>
/// CSA regression tests driven by a real PLK schedule fixture.
///
/// Station IDs are real PKP identifiers — look them up by searching the
/// fixture JSON for station names, or via the running app's station search.
///
/// To capture or refresh the fixture for a given date:
///   dotnet run --project TrainPlanner.FixtureCapture -- &lt;YYYY-MM-DD&gt;
/// Then commit the resulting Fixtures/&lt;YYYY-MM-DD&gt;.json file.
///
/// Assertions use exact DateTime values derived from the fixture — they are
/// intentionally strict. When a new fixture date is needed, add a new test
/// class rather than modifying existing ones, so old assertions remain valid.
/// </summary>
public class CsaAlgorithmRealWorldTests
{
    // ── Fixture ───────────────────────────────────────────────────────────────
    private static readonly DateOnly FixtureDate = new(2026, 7, 29);

    // Loaded once per test run — ConnectionBuilder + sort runs here.
    private static readonly List<JourneySegment> Legs =
        PlkFixture.LoadLegs("2026-07-29.json", FixtureDate);

    // ── Station IDs ───────────────────────────────────────────────────────────
    // TODO: replace placeholder values with real PKP station IDs from the fixture.
    // Hint: search the fixture JSON for station names (e.g. "Warszawa Centralna")
    // and note the corresponding "stationId" value.
    private const int StationA = 0; // e.g. Warszawa Centralna
    private const int StationB = 0; // e.g. Kraków Główny

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fixture_Loads_And_Contains_Legs()
    {
        // Smoke test: the fixture round-trips through deserialization and
        // ConnectionBuilder produces at least some usable graph edges.
        Assert.NotEmpty(Legs);
    }
}

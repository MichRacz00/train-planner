using Microsoft.Extensions.Logging.Abstractions;
using TrainPlanner.Models;
using TrainPlanner.Services;
using TrainPlanner.Tests.Fixtures;
using Xunit;

namespace TrainPlanner.Tests;

// ── Shared data types ─────────────────────────────────────────────────────────

/// <summary>Expected values for a single service leg within a journey.</summary>
public record ExpectedLeg(int From, int To, DateTime Departure, DateTime Arrival);

/// <summary>
/// Expected values for a complete journey returned by CsaPathfinder.
/// <see cref="Legs"/> is null when only dep/arr/leg-count are asserted.
/// </summary>
public record ExpectedJourney(
    string Label,
    DateTime Departure,
    DateTime Arrival,
    IReadOnlyList<ExpectedLeg>? Legs = null)
{
    // xUnit uses ToString() as the Theory display name
    public override string ToString() => Label;
}

// ── Test class ────────────────────────────────────────────────────────────────

/// <summary>
/// CSA regression tests driven by real PLK schedule fixtures.
///
/// <see cref="RunPathfinder"/> constructs a real <see cref="CsaPathfinder"/>
/// backed by <see cref="FixtureRouteSource"/> (no HTTP, no DI) and calls
/// <see cref="CsaPathfinder.FindTripsAsync"/>, collecting the full day's
/// journeys via the pathfinder's own internal loop.
///
/// Each [Theory] row is one expected journey. The test asserts exact
/// departure/arrival DateTimes and, where supplied, the per-leg
/// FromStation → ToStation + departure/arrival times.
///
/// To capture a fixture for a given date:
///   dotnet run --project TrainPlanner.FixtureCapture -- &lt;YYYY-MM-DD&gt;
/// Then commit the resulting TrainPlanner.Tests/Fixtures/&lt;YYYY-MM-DD&gt;.json.
/// </summary>
public class CsaAlgorithmRealWorldTests
{
    // ── Station IDs ───────────────────────────────────────────────────────────
    private const int WarsawaCentralna = 33605;
    private const int PoznanGlowny     = 30601;
    private const int ZielonaGoraGlowna = 27805;
    private const int WroclawGlowny    = 32201;
    private const int Leszno           = 42606;
    private const int Glogow           = 42200;

    // ── Warszawa Centralna → Głogów, 2026-07-31, dep after 16:00 ─────────────
    //
    // Source: PKP Intercity planner, queried 2026-07-29
    // Exact DateTimes (including fractional seconds from API) verified from fixture.
    //
    //  dep     arr        trains
    //  16:00   21:19:30   EIP 1850 + IC 2704 + IC 86152
    //  16:12   22:15      IC 1630 + KD 67423
    //  16:44   22:15      EIP 1604 + KD 67423
    //  17:00   21:34      EIC 40 + IC 1614 + KW 76203
    //  17:32   23:29      IC 1760 + R 76910
    //  18:00   23:29      EIC 1800 + IC 1760 + R 76910
    //  18:12   00:21+1    IC 1632 + KD 69481
    //  18:20   00:21+1    IC 5424 + IC 2600 + KD 69481
    //  19:32   01:12:24+1 IC 2706 + R 79101 + TLK 83194
    //  20:20   02:45+1    IC 1648 + TLK 38194

    private static readonly DateOnly D = new(2026, 7, 31);
    private static readonly DateOnly D1 = D.AddDays(1);

    private static DateTime T(DateOnly date, int h, int m, int s = 0) =>
        date.ToDateTime(new TimeOnly(h, m, s));

    public static IEnumerable<object[]> ExpectedJourneys()
    {
        // 16:00 → 21:19:30  Warszawa → Poznań → Zielona Góra → Głogów
        yield return Row(new ExpectedJourney(
            "16:00 EIP1850+IC2704+IC86152 → 21:19",
            Departure: T(D,  16,  0),
            Arrival:   T(D,  21, 19, 30),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,      T(D,  16,  0,  0), T(D,  18, 16, 30)),
                new(PoznanGlowny,       ZielonaGoraGlowna, T(D,  18, 20,  0), T(D,  20, 10,  0)),
                new(ZielonaGoraGlowna,  Glogow,            T(D,  20, 44,  0), T(D,  21, 19, 30)),
            ]));

        // 16:12 → 22:15  IC 1630 + KD 67423  (no named intermediate check)
        yield return Row(new ExpectedJourney(
            "16:12 IC1630+KD67423 → 22:15",
            Departure: T(D, 16, 12),
            Arrival:   T(D, 22, 15)));

        // 16:44 → 22:15  EIP 1604 + KD 67423  (no named intermediate check)
        yield return Row(new ExpectedJourney(
            "16:44 EIP1604+KD67423 → 22:15",
            Departure: T(D, 16, 44),
            Arrival:   T(D, 22, 15)));

        // 17:00 → 21:34  Warszawa → Poznań → Leszno → Głogów
        yield return Row(new ExpectedJourney(
            "17:00 EIC40+IC1614+KW76203 → 21:34",
            Departure: T(D,  17,  0),
            Arrival:   T(D,  21, 34),
            Legs: [
                new(WarsawaCentralna, PoznanGlowny, T(D,  17,  0,  0), T(D,  19, 24,  0)),
                new(PoznanGlowny,     Leszno,       T(D,  19, 31,  0), T(D,  20,  3, 12)),
                new(Leszno,           Glogow,       T(D,  20, 52,  0), T(D,  21, 34,  0)),
            ]));

        // 17:32 → 23:29  Warszawa → Zielona Góra → Głogów
        yield return Row(new ExpectedJourney(
            "17:32 IC1760+R76910 → 23:29",
            Departure: T(D,  17, 32),
            Arrival:   T(D,  23, 29),
            Legs: [
                new(WarsawaCentralna,  ZielonaGoraGlowna, T(D,  17, 32,  0), T(D,  22, 25,  0)),
                new(ZielonaGoraGlowna, Glogow,            T(D,  22, 43,  0), T(D,  23, 29,  0)),
            ]));

        // 18:00 → 23:29  Warszawa → Poznań → Zielona Góra → Głogów
        yield return Row(new ExpectedJourney(
            "18:00 EIC1800+IC1760+R76910 → 23:29",
            Departure: T(D,  18,  0),
            Arrival:   T(D,  23, 29),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,      T(D,  18,  0,  0), T(D,  20, 18,  0)),
                new(PoznanGlowny,       ZielonaGoraGlowna, T(D,  20, 40,  0), T(D,  22, 25,  0)),
                new(ZielonaGoraGlowna,  Glogow,            T(D,  22, 43,  0), T(D,  23, 29,  0)),
            ]));

        // 18:12 → 00:21+1  IC 1632 + KD 69481  (no named intermediate check)
        yield return Row(new ExpectedJourney(
            "18:12 IC1632+KD69481 → 00:21+1",
            Departure: T(D,  18, 12),
            Arrival:   T(D1,  0, 21)));

        // 18:20 → 00:21+1  IC 5424 + IC 2600 + KD 69481  (no named intermediate check)
        yield return Row(new ExpectedJourney(
            "18:20 IC5424+IC2600+KD69481 → 00:21+1",
            Departure: T(D,  18, 20),
            Arrival:   T(D1,  0, 21)));

        // 19:32 → 01:12:24+1  Warszawa → Poznań → Zielona Góra → Głogów
        yield return Row(new ExpectedJourney(
            "19:32 IC2706+R79101+TLK83194 → 01:12+1",
            Departure: T(D,  19, 32),
            Arrival:   T(D1,  1, 12, 24),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,      T(D,   19, 32,  0), T(D,   22, 16,  0)),
                new(PoznanGlowny,       ZielonaGoraGlowna, T(D,   22, 21,  0), T(D,   23, 55,  0)),
                new(ZielonaGoraGlowna,  Glogow,            T(D1,   0, 34,  0), T(D1,   1, 12, 24)),
            ]));

        // 20:20 → 02:45+1  IC 1648 + TLK 38194  (no named intermediate check)
        yield return Row(new ExpectedJourney(
            "20:20 IC1648+TLK38194 → 02:45+1",
            Departure: T(D,  20, 20),
            Arrival:   T(D1,  2, 45)));
    }

    private static object[] Row(ExpectedJourney j) => [j];

    // ── Shared pathfinder helper ──────────────────────────────────────────────

    private static async Task<IReadOnlyList<Journey>> RunPathfinder(
        string fixture, int from, int to, DateOnly date, TimeOnly after)
    {
        var source = new FixtureRouteSource(fixture);
        var pf     = new CsaPathfinder(source, NullLogger<CsaPathfinder>.Instance);
        return await pf.FindTripsAsync(from, to, date, after);
    }

    // ── Test ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ExpectedJourneys))]
    public async Task WarsawaCentralna_To_Glogow_20260731(ExpectedJourney expected)
    {
        var results = await RunPathfinder(
            "2026-07-31.json", WarsawaCentralna, Glogow, D, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == expected.Departure &&
            j.Arrival   == expected.Arrival   &&
            LegsMatch(j.Legs, expected.Legs));
    }

    private static bool LegsMatch(
        IReadOnlyList<JourneySegment> actual,
        IReadOnlyList<ExpectedLeg>?   expected)
    {
        if (expected is null) return true;
        if (actual.Count != expected.Count) return false;
        for (var i = 0; i < expected.Count; i++)
        {
            var a = actual[i];
            var e = expected[i];
            if (a.FromStationId != e.From      ||
                a.ToStationId   != e.To        ||
                a.Departure     != e.Departure ||
                a.Arrival       != e.Arrival)
                return false;
        }
        return true;
    }
}

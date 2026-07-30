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
/// <see cref="Legs"/> is null when only dep/arr are asserted (no intermediate check).
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
/// FromStation → ToStation + departure/arrival times from the fixture.
///
/// Rules for included journeys (source: PKP Intercity planner 2026-07-29):
///   - Departure ≥ 16:00
///   - No leg departs after midnight (00:00+1)
///
/// To capture a fixture for a given date:
///   dotnet run --project TrainPlanner.FixtureCapture -- &lt;YYYY-MM-DD&gt;
/// Then commit the resulting TrainPlanner.Tests/Fixtures/&lt;YYYY-MM-DD&gt;.json.
/// </summary>
public class CsaAlgorithmRealWorldTests
{
    // ── Station IDs ───────────────────────────────────────────────────────────
    private const int WarsawaCentralna  = 33605;
    private const int PoznanGlowny      = 30601;
    private const int ZielonaGoraGlowna = 27805;
    private const int WroclawGlowny     = 32201;
    private const int Leszno            = 42606;
    private const int Glogow            = 42200;
    private const int KrakowGlowny      = 80416;
    private const int Katowice          = 73312;
    private const int WloszczowaPolnoc  = 64899;
    private const int Skierniewice      = 47001;
    private const int Kutno             = 32201;
    private const int LowiczGlowny      = 32904;
    private const int LodzWidzew        = 46409;
    private const int WarsawaZachodnia  = 33506;

    // ── Warszawa Centralna → Głogów, 2026-07-31, dep ≥ 16:00 ─────────────────
    //
    // Source of truth: PKP Intercity planner, queried 2026-07-29
    // Intermediate leg times verified against fixture via diagnostic dump.
    // Journeys with any leg departing after midnight are excluded.
    //
    //  dep    arr      trains                        intermediate check
    //  16:00  21:19    EIP 1850 + IC 2704 + IC 86152  W→Poz→ZG→G  (fixture routing)
    //  16:12  22:15    IC 1630 + KD 67423             W→Wro→G      (CSA routes via 33506, no named intermediate)
    //  16:44  22:15    EIP 1604 + KD 67423            W→Wro→G      (CSA routes directly, no named intermediate)
    //  17:00  21:34    EIC 40 + IC 1614 + KW 76203    W→Poz→Les→G (fixture routing)
    //  17:32  23:29    IC 1760 + R 76910              W→ZG→G
    //  18:00  23:29    EIC 1800 + IC 1760 + R 76910   W→Poz→ZG→G
    //  18:12  00:21+1  IC 1632 + KD 69481             W→Wro→G      (CSA routes via 59105, no named intermediate)
    //  18:20  00:21+1  IC 5424 + IC 2600 + KD 69481   (bus transfer – CSA finds alt routing, no named intermediate)
    //  excluded: 19:32 → ZG→G leg departs 00:34 (after midnight)
    //  excluded: 20:20 → Wro→G leg departs 01:19 (after midnight)

    private static readonly DateOnly D  = new(2026, 7, 31);
    private static readonly DateOnly D1 = D.AddDays(1);

    private static DateTime T(DateOnly d, int h, int m, int s = 0) =>
        d.ToDateTime(new TimeOnly(h, m, s));

    public static IEnumerable<object[]> ExpectedJourneys()
    {
        // 16:00 → 21:19   EIP 1850 → Poznań → Regional (earlier connection) → Zielona Góra → IC 86152 → Głogów
        // CSA chooses the earlier regional Poz→ZG connection (dep 18:20)
        yield return Row(new ExpectedJourney(
            "16:00 EIP1850+Regional(18:20)+IC86152 → 21:19 (regional)",
            Departure: T(D, 16,  0),
            Arrival:   T(D, 21, 19, 30),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,       T(D, 16,  0,  0), T(D, 18, 16, 30)),
                new(PoznanGlowny,       ZielonaGoraGlowna,  T(D, 18, 20,  0), T(D, 20, 10,  0)),
                new(ZielonaGoraGlowna,  Glogow,             T(D, 20, 44,  0), T(D, 21, 19, 30)),
            ]));

        // 16:00 → 21:19   EIP 1850 → Poznań → IC 2704 → Zielona Góra → IC 86152 → Głogów
        // PKP planner chooses IC 2704 (the named InterCity connection)
        // W→Poz 16:00→18:16, Poz→ZG 19:05→20:33, ZG→G 20:44→21:19
        yield return Row(new ExpectedJourney(
            "16:00 EIP1850+IC2704+IC86152 → 21:19 (intercity)",
            Departure: T(D, 16,  0),
            Arrival:   T(D, 21, 19, 30),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,       T(D, 16,  0,  0), T(D, 18, 16, 30)),
                new(PoznanGlowny,       ZielonaGoraGlowna,  T(D, 19,  5,  0), T(D, 20, 33,  0)),
                new(ZielonaGoraGlowna,  Glogow,             T(D, 20, 44,  0), T(D, 21, 19, 30)),
            ]));

        // 16:12 → 22:15   IC 1630 → Wrocław → KD 67423 → Głogów
        yield return Row(new ExpectedJourney(
            "16:12 IC1630+KD67423 → 22:15",
            Departure: T(D, 16, 12),
            Arrival:   T(D, 22, 15),
            Legs: [
                new(WarsawaCentralna, WroclawGlowny, T(D, 16, 12,  0), T(D, 20, 30,  0)),
                new(WroclawGlowny,    Glogow,        T(D, 20, 44,  0), T(D, 22, 15,  0)),
            ]));

        // 16:44 → 22:15   EIP 1604 → Wrocław → KD 67423 → Głogów
        // CSA routes directly W→60103. No named intermediate check.
        yield return Row(new ExpectedJourney(
            "16:44 EIP1604+KD67423 → 22:15",
            Departure: T(D, 16, 44),
            Arrival:   T(D, 22, 15)));

        // 16:44 → 22:15   EIP 1604 → Wrocław → KD 67423 → Głogów
        // PKP planner routing: W→Wro 16:44→20:13, Wro→G 20:44→22:15
        yield return Row(new ExpectedJourney(
            "16:44 EIP1604+KD67423 → 22:15 (via Wrocław)",
            Departure: T(D, 16, 44),
            Arrival:   T(D, 22, 15),
            Legs: [
                new(WarsawaCentralna, WroclawGlowny, T(D, 16, 44,  0), T(D, 20, 13,  0)),
                new(WroclawGlowny,    Glogow,        T(D, 20, 44,  0), T(D, 22, 15,  0)),
            ]));

        // 17:00 → 21:34   EIC 40 → Poznań → IC 1614 → Leszno → KW 76203 → Głogów
        // PKP planner routing verified: W→Poz 17:00→19:24, Poz→Les 19:35→20:09, Les→G 20:52→21:34
        yield return Row(new ExpectedJourney(
            "17:00 EIC40+IC1614+KW76203 → 21:34",
            Departure: T(D, 17,  0),
            Arrival:   T(D, 21, 34),
            Legs: [
                new(WarsawaCentralna, PoznanGlowny, T(D, 17,  0,  0), T(D, 19, 24,  0)),
                new(PoznanGlowny,     Leszno,       T(D, 19, 35,  0), T(D, 20,  9,  0)),
                new(Leszno,           Glogow,       T(D, 20, 52,  0), T(D, 21, 34,  0)),
            ]));

        // 17:32 → 23:29   IC 1760 → Zielona Góra → R 76910 → Głogów
        yield return Row(new ExpectedJourney(
            "17:32 IC1760+R76910 → 23:29",
            Departure: T(D, 17, 32),
            Arrival:   T(D, 23, 29),
            Legs: [
                new(WarsawaCentralna,  ZielonaGoraGlowna, T(D, 17, 32,  0), T(D, 22, 25,  0)),
                new(ZielonaGoraGlowna, Glogow,            T(D, 22, 43,  0), T(D, 23, 29,  0)),
            ]));

        // 18:00 → 23:29   EIC 1800 → Poznań → IC 1760 → Zielona Góra → R 76910 → Głogów
        yield return Row(new ExpectedJourney(
            "18:00 EIC1800+IC1760+R76910 → 23:29",
            Departure: T(D, 18,  0),
            Arrival:   T(D, 23, 29),
            Legs: [
                new(WarsawaCentralna,   PoznanGlowny,       T(D, 18,  0,  0), T(D, 20, 18,  0)),
                new(PoznanGlowny,       ZielonaGoraGlowna,  T(D, 20, 40,  0), T(D, 22, 25,  0)),
                new(ZielonaGoraGlowna,  Glogow,             T(D, 22, 43,  0), T(D, 23, 29,  0)),
            ]));

        // 18:12 → 00:21+1   IC 1632 → Wrocław → KD 69481 → Głogów
        // CSA routes via station 59105 area. No named intermediate check.
        yield return Row(new ExpectedJourney(
            "18:12 IC1632+KD69481 → 00:21+1",
            Departure: T(D,  18, 12),
            Arrival:   T(D1,  0, 21)));

        // 18:12 → 00:21+1   IC 1632 → Wrocław → KD 69481 → Głogów
        // PKP planner routing: W→Wro 18:12→22:29, Wro→G 22:50→00:21+1
        yield return Row(new ExpectedJourney(
            "18:12 IC1632+KD69481 → 00:21+1 (via Wrocław)",
            Departure: T(D,  18, 12),
            Arrival:   T(D1,  0, 21),
            Legs: [
                new(WarsawaCentralna, WroclawGlowny, T(D, 18, 12,  0), T(D, 22, 29,  0)),
                new(WroclawGlowny,    Glogow,        T(D, 22, 50,  0), T(D1,  0, 21,  0)),
            ]));

        // 18:20 → 00:21+1   IC 5424 + IC 2600 + KD 69481
        // PKP routing includes a bus transfer (Częstochowa → Częstochowa Stradom)
        // absent from the PLK train-only fixture. CSA finds an all-rail alternate
        // with the same dep/arr. No named intermediate check.
        yield return Row(new ExpectedJourney(
            "18:20 IC5424+IC2600+KD69481 → 00:21+1",
            Departure: T(D,  18, 20),
            Arrival:   T(D1,  0, 21)));
    }

    private static object[] Row(ExpectedJourney j) => [j];

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Journey>> RunPathfinder(
        string fixture, int from, int to, DateOnly date, TimeOnly after)
    {
        var source = new FixtureRouteSource(fixture);
        var pf     = new CsaPathfinder(source, NullLogger<CsaPathfinder>.Instance);
        return await pf.FindTripsAsync(from, to, date, after);
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
            var depMatch = a.Departure.Hour == e.Departure.Hour &&
                          a.Departure.Minute == e.Departure.Minute;
            var arrMatch = a.Arrival.Hour == e.Arrival.Hour &&
                          a.Arrival.Minute == e.Arrival.Minute;
            if (a.FromStationId != e.From      ||
                a.ToStationId   != e.To        ||
                !depMatch ||
                !arrMatch)
                return false;
        }
        return true;
    }

    // ── Warszawa Centralna → Kraków Główny, 2026-07-31, dep ≥ 13:00 ────────────
    //
    // Source of truth: PKP Intercity planner, queried 2026-07-29
    //
    //  dep    arr      trains                       intermediate check
    //  12:08  16:32    IC 5328 (KOLBERG)            W→K direct
    //  12:36  16:06    EIP 8452 + IC 59 (GALICJA)   W→Katowice→K
    //  13:28  16:55    IC 8328 (LUBOMIRSKI)         W→K direct
    //  13:44  16:35    EIP 5302                     W→K direct (fastest)
    //  14:04  17:11    IC 107 + IC 8322             W→Włoszczowa→K
    //  14:08  18:33    IC 5326 (SIENKIEWICZ)        W→K direct

    private static readonly DateOnly D2  = new(2026, 7, 31);

    public static IEnumerable<object[]> ExpectedJourneysWawKrak()
    {
        // 12:08 → 16:32   IC 5328 → Kraków
        yield return Row(new ExpectedJourney(
            "12:08 IC5328 → 16:32",
            Departure: T(D2, 12,  8),
            Arrival:   T(D2, 16, 32)));

        // 12:36 → 16:06   EIP 8452 → Katowice → IC 59 → Kraków
        yield return Row(new ExpectedJourney(
            "12:36 EIP8452+IC59 → 16:06",
            Departure: T(D2, 12, 36),
            Arrival:   T(D2, 16,  6),
            Legs: [
                new(WarsawaCentralna, Katowice,    T(D2, 12, 36,  0), T(D2, 15,  6,  0)),
                new(Katowice,         KrakowGlowny, T(D2, 15, 16,  0), T(D2, 16,  6,  0)),
            ]));

        // 13:28 → 16:55   IC 8328 → Kraków
        yield return Row(new ExpectedJourney(
            "13:28 IC8328 → 16:55",
            Departure: T(D2, 13, 28),
            Arrival:   T(D2, 16, 55)));

        // 13:44 → 16:35   EIP 5302 → Kraków (fastest)
        yield return Row(new ExpectedJourney(
            "13:44 EIP5302 → 16:35",
            Departure: T(D2, 13, 44),
            Arrival:   T(D2, 16, 35)));

        // 14:04 → 17:11   IC 107 → Włoszczowa → IC 8322 → Kraków
        yield return Row(new ExpectedJourney(
            "14:04 IC107+IC8322 → 17:11",
            Departure: T(D2, 14,  4),
            Arrival:   T(D2, 17, 11),
            Legs: [
                new(WarsawaCentralna,  WloszczowaPolnoc, T(D2, 14,  4,  0), T(D2, 15, 30,  0)),
                new(WloszczowaPolnoc,  KrakowGlowny,     T(D2, 15, 36,  0), T(D2, 17, 11,  0)),
            ]));

         // 14:08 → 18:33   IC 5326 → Kraków
         yield return Row(new ExpectedJourney(
             "14:08 IC5326 → 18:33",
             Departure: T(D2, 14,  8),
             Arrival:   T(D2, 18, 33)));
     }

     // ── Skierniewice → Kutno, 2026-07-31, dep ≥ 13:00 ─────────────────────────
     //
     // Source of truth: PKP Intercity planner, queried 2026-07-29
     //
     //  dep    arr      trains                               intermediate check
     //  12:29  13:23    L 99665 (ŁKA)                       S→K direct
     //  13:08  14:37    Bus19271 + IC 145                   S→Łowicz→K (bus + IC)
     //  13:08  15:16    Bus19271 (ŁKA)                      S→K direct (bus only)
     //  13:11  15:00    IC 6128 + EIC 1612                  S→Warszawa→K
     //  14:15  15:37    L 99669 + IC 3834                   S→Łowicz→K
     //  14:21  16:00    IR 10138 + EIC 42                   S→Warszawa→K
     //  14:45  16:37    Bus99687 + IC 2704                  S→Łowicz→K (bus + IC)
     //  14:45  16:53    Bus99687 (ŁKA)                      S→K direct (bus only)
     //  15:23  17:28    IC 1904 + IC 4522                   S→Łódź→K
     //  15:41  17:39    Bus11273 + IC 2524                  S→Łowicz→K (bus + IC)
     //  17:00  17:53    L 99675 (ŁKA)                       S→K direct

     public static IEnumerable<object[]> ExpectedJourneysSkierKutno()
     {
         // 12:29 → 13:23   L 99665 → Kutno
         yield return Row(new ExpectedJourney(
             "12:29 L99665 → 13:23",
             Departure: T(D2, 12, 29),
             Arrival:   T(D2, 13, 23)));

         // 13:08 → 14:37   Bus19271 → Łowicz → IC 145 → Kutno
         yield return Row(new ExpectedJourney(
             "13:08 Bus19271+IC145 → 14:37",
             Departure: T(D2, 13,  8),
             Arrival:   T(D2, 14, 37),
             Legs: [
                 new(Skierniewice,  LowiczGlowny, T(D2, 13,  8,  0), T(D2, 14,  4,  0)),
                 new(LowiczGlowny,  Kutno,        T(D2, 14, 17,  0), T(D2, 14, 37,  0)),
             ]));

         // 13:08 → 15:16   Bus19271 → Kutno (bus only)
         yield return Row(new ExpectedJourney(
             "13:08 Bus19271 → 15:16",
             Departure: T(D2, 13,  8),
             Arrival:   T(D2, 15, 16)));

         // 13:11 → 15:00   IC 6128 → Warszawa → EIC 1612 → Kutno
         yield return Row(new ExpectedJourney(
             "13:11 IC6128+EIC1612 → 15:00",
             Departure: T(D2, 13, 11),
             Arrival:   T(D2, 15,  0),
             Legs: [
                 new(Skierniewice,       WarsawaZachodnia, T(D2, 13, 11,  0), T(D2, 13, 42,  0)),
                 new(WarsawaZachodnia,   Kutno,            T(D2, 14,  5,  0), T(D2, 15,  0,  0)),
             ]));

         // 14:15 → 15:37   L 99669 → Łowicz → IC 3834 → Kutno
         yield return Row(new ExpectedJourney(
             "14:15 L99669+IC3834 → 15:37",
             Departure: T(D2, 14, 15),
             Arrival:   T(D2, 15, 37),
             Legs: [
                 new(Skierniewice,  LowiczGlowny, T(D2, 14, 15,  0), T(D2, 14, 35,  0)),
                 new(LowiczGlowny,  Kutno,        T(D2, 15, 17,  0), T(D2, 15, 37,  0)),
             ]));

         // 14:21 → 16:00   IR 10138 → Warszawa → EIC 42 → Kutno
         yield return Row(new ExpectedJourney(
             "14:21 IR10138+EIC42 → 16:00",
             Departure: T(D2, 14, 21),
             Arrival:   T(D2, 16,  0),
             Legs: [
                 new(Skierniewice,       WarsawaZachodnia, T(D2, 14, 21,  0), T(D2, 15,  1,  0)),
                 new(WarsawaZachodnia,   Kutno,            T(D2, 15,  5,  0), T(D2, 16,  0,  0)),
             ]));

         // 14:45 → 16:37   Bus99687 → Łowicz → IC 2704 → Kutno
         yield return Row(new ExpectedJourney(
             "14:45 Bus99687+IC2704 → 16:37",
             Departure: T(D2, 14, 45),
             Arrival:   T(D2, 16, 37),
             Legs: [
                 new(Skierniewice,  LowiczGlowny, T(D2, 14, 45,  0), T(D2, 15, 41,  0)),
                 new(LowiczGlowny,  Kutno,        T(D2, 16, 17,  0), T(D2, 16, 37,  0)),
             ]));

         // 14:45 → 16:53   Bus99687 → Kutno (bus only)
         yield return Row(new ExpectedJourney(
             "14:45 Bus99687 → 16:53",
             Departure: T(D2, 14, 45),
             Arrival:   T(D2, 16, 53)));

         // 15:23 → 17:28   IC 1904 → Łódź → IC 4522 → Kutno
         yield return Row(new ExpectedJourney(
             "15:23 IC1904+IC4522 → 17:28",
             Departure: T(D2, 15, 23),
             Arrival:   T(D2, 17, 28),
             Legs: [
                 new(Skierniewice,  LodzWidzew, T(D2, 15, 23,  0), T(D2, 15, 53,  0)),
                 new(LodzWidzew,    Kutno,      T(D2, 16, 17,  0), T(D2, 17, 28,  0)),
             ]));

         // 15:41 → 17:39   Bus11273 → Łowicz → IC 2524 → Kutno
         yield return Row(new ExpectedJourney(
             "15:41 Bus11273+IC2524 → 17:39",
             Departure: T(D2, 15, 41),
             Arrival:   T(D2, 17, 39),
             Legs: [
                 new(Skierniewice,  LowiczGlowny, T(D2, 15, 41,  0), T(D2, 16, 37,  0)),
                 new(LowiczGlowny,  Kutno,        T(D2, 17, 17,  0), T(D2, 17, 39,  0)),
             ]));

         // 17:00 → 17:53   L 99675 → Kutno
         yield return Row(new ExpectedJourney(
             "17:00 L99675 → 17:53",
             Departure: T(D2, 17,  0),
             Arrival:   T(D2, 17, 53)));
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

    [Theory]
    [MemberData(nameof(ExpectedJourneysWawKrak))]
    public async Task WarsawaCentralna_To_KrakowGlowny_20260731(ExpectedJourney expected)
    {
        var results = await RunPathfinder(
            "2026-07-31.json", WarsawaCentralna, KrakowGlowny, D2, new TimeOnly(12, 0));

        Assert.Contains(results, j =>
            j.Departure == expected.Departure &&
            j.Arrival   == expected.Arrival   &&
            LegsMatch(j.Legs, expected.Legs));
    }

    [Theory]
    [MemberData(nameof(ExpectedJourneysSkierKutno))]
    public async Task Skierniewice_To_Kutno_20260731(ExpectedJourney expected)
    {
        var results = await RunPathfinder(
            "2026-07-31.json", Skierniewice, Kutno, D2, new TimeOnly(12, 0));

        Assert.Contains(results, j =>
            j.Departure == expected.Departure &&
            j.Arrival   == expected.Arrival   &&
            LegsMatch(j.Legs, expected.Legs));
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using TrainPlanner.Models;
using TrainPlanner.Services;
using TrainPlanner.Tests.Fixtures;
using Xunit;

namespace TrainPlanner.Tests;

/// <summary>
/// CSA regression tests driven by real PLK schedule fixtures.
///
/// Each test is fully self-contained — it declares its own fixture date,
/// station IDs, and expected departure/arrival.
///
/// The helper <see cref="RunPathfinder"/> constructs a real <see cref="CsaPathfinder"/>
/// backed by <see cref="FixtureRouteSource"/> (no HTTP, no DI) and calls
/// <see cref="CsaPathfinder.FindTripsAsync"/> with dep=16:00, collecting the full
/// day's journeys via the pathfinder's own loop. Each [Fact] then asserts that
/// its specific journey exists in that full result set.
///
/// To capture a fixture for a given date:
///   dotnet run --project TrainPlanner.FixtureCapture -- &lt;YYYY-MM-DD&gt;
/// Then commit the resulting TrainPlanner.Tests/Fixtures/&lt;YYYY-MM-DD&gt;.json.
/// </summary>
public class CsaAlgorithmRealWorldTests
{
    // ── Warszawa Centralna → Głogów, 2026-07-31, dep after 16:00 ─────────────
    //
    // Source: PKP Intercity planner, queried 2026-07-29
    //
    //  dep     arr       trains
    //  16:00   21:19     EIP 1850 + IC 2704 + IC 86152
    //  16:12   22:15     IC 1630 + KD 67423
    //  16:44   22:15     EIP 1604 + KD 67423
    //  17:00   21:34     EIC 40 + IC 1614 + KW 76203
    //  17:32   23:29     IC 1760 + R 76910
    //  18:00   23:29     EIC 1800 + IC 1760 + R 76910
    //  18:12   00:21+1   IC 1632 + KD 69481
    //  18:20   00:21+1   IC 5424 + IC 2600 + KD 69481
    //  19:32   01:12+1   IC 2706 + R 79101 + TLK 83194
    //  20:20   02:45+1   IC 1648 + TLK 38194

    private static async Task<IReadOnlyList<Journey>> RunPathfinder(
        string fixture, int from, int to, DateOnly date, TimeOnly after)
    {
        var source = new FixtureRouteSource(fixture);
        var pf     = new CsaPathfinder(source, NullLogger<CsaPathfinder>.Instance);
        return await pf.FindTripsAsync(from, to, date, after);
    }

    [Fact]
    public async Task Journey_1600_EIP1850_IC2704_IC86152_ArrivesAt2119()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(16, 0)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(21, 19)));
    }

    [Fact]
    public async Task Journey_1612_IC1630_KD67423_ArrivesAt2215()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(16, 12)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(22, 15)));
    }

    [Fact]
    public async Task Journey_1644_EIP1604_KD67423_ArrivesAt2215()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(16, 44)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(22, 15)));
    }

    [Fact]
    public async Task Journey_1700_EIC40_IC1614_KW76203_ArrivesAt2134()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(17, 0)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(21, 34)));
    }

    [Fact]
    public async Task Journey_1732_IC1760_R76910_ArrivesAt2329()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(17, 32)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(23, 29)));
    }

    [Fact]
    public async Task Journey_1800_EIC1800_IC1760_R76910_ArrivesAt2329()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(18, 0)) &&
            j.Arrival   == date.ToDateTime(new TimeOnly(23, 29)));
    }

    [Fact]
    public async Task Journey_1812_IC1632_KD69481_ArrivesAt0021NextDay()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(18, 12)) &&
            j.Arrival   == date.AddDays(1).ToDateTime(new TimeOnly(0, 21)));
    }

    [Fact]
    public async Task Journey_1820_IC5424_IC2600_KD69481_ArrivesAt0021NextDay()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(18, 20)) &&
            j.Arrival   == date.AddDays(1).ToDateTime(new TimeOnly(0, 21)));
    }

    [Fact]
    public async Task Journey_1932_IC2706_R79101_TLK83194_ArrivesAt0112NextDay()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(19, 32)) &&
            j.Arrival   == date.AddDays(1).ToDateTime(new TimeOnly(1, 12)));
    }

    [Fact]
    public async Task Journey_2020_IC1648_TLK38194_ArrivesAt0245NextDay()
    {
        var date = new DateOnly(2026, 7, 31);
        const int warsawaCentralna = 33605;
        const int glogow           = 42200;

        var results = await RunPathfinder("2026-07-31.json", warsawaCentralna, glogow,
            date, new TimeOnly(16, 0));

        Assert.Contains(results, j =>
            j.Departure == date.ToDateTime(new TimeOnly(20, 20)) &&
            j.Arrival   == date.AddDays(1).ToDateTime(new TimeOnly(2, 45)));
    }
}

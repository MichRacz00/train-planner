using TrainPlanner.Models;
using TrainPlanner.Services;
using Xunit;

namespace TrainPlanner.Tests;

/// <summary>
/// Builds minimal JourneySegment legs for CSA tests.
/// Uses a fixed base date so times read as HH:mm in test names.
/// </summary>
internal static class Leg
{
    internal static readonly DateTime Base = new(2026, 7, 28);

    internal static JourneySegment Make(
        int from, int to,
        int depH, int depM,
        int arrH, int arrM,
        int scheduleId = 1, int orderId = 1,
        string carrier = "IC",
        int depDay = 0, int arrDay = 0)
        => new()
        {
            FromStationId      = from,
            ToStationId        = to,
            Departure          = Base.AddDays(depDay).AddHours(depH).AddMinutes(depM),
            Arrival            = Base.AddDays(arrDay).AddHours(arrH).AddMinutes(arrM),
            ScheduleId         = scheduleId,
            OrderId            = orderId,
            CarrierCode        = carrier,
            CommercialCategory = carrier,
        };

    internal static List<JourneySegment> Sorted(params JourneySegment[] legs)
        => [.. legs.OrderBy(l => l.Departure)];

    internal static DateTime At(int h, int m, int day = 0)
        => Base.AddDays(day).AddHours(h).AddMinutes(m);
}

public class CsaAlgorithmTests
{
    // ── Station IDs ───────────────────────────────────────────────────────────
    const int WAW = 1; // Warszawa
    const int POZ = 2; // Poznań
    const int WRO = 3; // Wrocław
    const int GLO = 4; // Głogów
    const int KRK = 5; // Kraków
    const int MID = 6; // generic intermediate station

    // =========================================================================
    // Basic connectivity
    // =========================================================================

    [Fact]
    public void NoLegs_ReturnsEmpty()
    {
        var result = CsaAlgorithm.FindJourneys([], WAW, KRK, Leg.At(6, 0));

        Assert.Empty(result);
    }

    [Fact]
    public void DirectTrain_Found()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, KRK, 6, 0, 9, 0));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Single(result);
        Assert.Equal(0, result[0].Transfers);
        Assert.Equal(Leg.At(9, 0), result[0].Arrival);
    }

    [Fact]
    public void TrainDepartsBeforeEarliestDeparture_NotFound()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, KRK, 5, 0, 8, 0));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Empty(result);
    }

    [Fact]
    public void MultiLegThroughTrain_CountsAsZeroTransfers()
    {
        // Two consecutive legs on the SAME scheduleId/orderId = one through train
        var legs = Leg.Sorted(
            Leg.Make(WAW, MID, 6, 0,  8, 0, scheduleId: 1, orderId: 1),
            Leg.Make(MID, KRK, 8, 5, 10, 0, scheduleId: 1, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Single(result);
        Assert.Equal(0, result[0].Transfers);
        Assert.Equal(Leg.At(10, 0), result[0].Arrival);
    }

    [Fact]
    public void UnreachableDestination_ReturnsEmpty()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, POZ, 6, 0, 8, 0)); // only WAW->POZ, not KRK

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Empty(result);
    }

    // =========================================================================
    // Transfers
    // =========================================================================

    [Fact]
    public void OneTransfer_Found()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, POZ, 6, 0,  8,  0, scheduleId: 1, orderId: 1),
            Leg.Make(POZ, KRK, 8, 10, 11, 0, scheduleId: 2, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Single(result);
        Assert.Equal(1, result[0].Transfers);
        Assert.Equal(Leg.At(11, 0), result[0].Arrival);
    }

    [Fact]
    public void MissedConnection_NotBoarded()
    {
        // Connecting train departs before first train arrives
        var legs = Leg.Sorted(
            Leg.Make(WAW, POZ, 6, 0, 8,  0, scheduleId: 1, orderId: 1),
            Leg.Make(POZ, KRK, 7, 0, 10, 0, scheduleId: 2, orderId: 1)); // departs 07:00, arrives 08:00

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Empty(result);
    }

    [Fact]
    public void CycleDetection_SameStationNotVisitedTwice()
    {
        // WAW -> MID -> POZ -> MID is a loop; second visit to MID must be skipped
        var legs = Leg.Sorted(
            Leg.Make(WAW, MID, 6,  0, 7,  0, scheduleId: 1, orderId: 1),
            Leg.Make(MID, POZ, 7, 10, 8,  0, scheduleId: 2, orderId: 1),
            Leg.Make(POZ, MID, 8, 10, 9,  0, scheduleId: 3, orderId: 1),
            Leg.Make(MID, KRK, 9, 10, 11, 0, scheduleId: 4, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Single(result);
        Assert.Equal(Leg.At(11, 0), result[0].Arrival);
    }

    // =========================================================================
    // Dominance — the core correctness properties
    // =========================================================================

    [Fact]
    public void DirectTrain_NotDominatedByFasterConnectionWithTransfer()
    {
        // Direct IC:   departs 06:08 WAW, arrives 10:05 KRK, 0 transfers
        // EIP+connect: departs 06:08 WAW, arrives 09:38 KRK, 1 transfer
        // Both should survive — neither dominates the other
        var legs = Leg.Sorted(
            // Direct IC through two segments (same scheduleId)
            Leg.Make(WAW, MID, 6,  8,  6, 12, scheduleId: 10, orderId: 1, carrier: "IC"),
            Leg.Make(MID, KRK, 6, 12, 10,  5, scheduleId: 10, orderId: 1, carrier: "IC"),
            // EIP to intermediate, then fast onward train
            Leg.Make(WAW, MID, 6,  8,  6, 12, scheduleId: 20, orderId: 1, carrier: "EIP"),
            Leg.Make(MID, KRK, 6, 45,  9, 38, scheduleId: 30, orderId: 1, carrier: "EIP"));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 8));

        Assert.Contains(result, j => j.Transfers == 0 && j.Arrival == Leg.At(10, 5));
        Assert.Contains(result, j => j.Transfers == 1 && j.Arrival == Leg.At(9, 38));
    }

    [Fact]
    public void StrictlyDominatedJourney_NotReturned()
    {
        // A: departs 06:40, arrives 09:38, 0 transfers (direct EIP)
        // B: departs 06:00, arrives 09:38, 1 transfer (slow regional + EIP)
        // B is dominated: A departs later, same arrival, fewer transfers
        var legs = Leg.Sorted(
            // A: direct
            Leg.Make(WAW, KRK, 6, 40, 9, 38, scheduleId: 1, orderId: 1, carrier: "EIP"),
            // B: regional feeder then same EIP
            Leg.Make(WAW, MID, 6,  0, 6, 30, scheduleId: 2, orderId: 1, carrier: "R"),
            Leg.Make(MID, KRK, 6, 45, 9, 38, scheduleId: 3, orderId: 1, carrier: "EIP"));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Contains(result, j => j.Transfers == 0 && j.Departure == Leg.At(6, 40));
        Assert.DoesNotContain(result, j =>
            j.Transfers == 1 &&
            j.Departure == Leg.At(6, 0) &&
            j.Arrival   == Leg.At(9, 38));
    }

    [Fact]
    public void TwoJourneysSameArrival_DirectTrainAlwaysSurvives()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, KRK, 9, 40, 12, 29, scheduleId: 1, orderId: 1),
            Leg.Make(WAW, MID, 9, 24,  9, 28, scheduleId: 2, orderId: 1, carrier: "S3"),
            Leg.Make(MID, KRK, 9, 45, 12, 29, scheduleId: 3, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(9, 0));

        Assert.Contains(result, j => j.Transfers == 0 && j.Arrival == Leg.At(12, 29));
    }

    // =========================================================================
    // The Głogów scenario: 1-transfer via regional operator must not be pruned
    // =========================================================================

    [Fact]
    public void GlogowScenario_OneTransferViaRegionalOperatorFound()
    {
        // IC 16:12 WAW -> POZ -> KD regional -> GLO 22:15  (1 transfer)
        // IC 16:44 WAW -> WRO -> Os -> GLO 22:15           (2 transfers)
        // The 1-transfer route must survive
        var legs = Leg.Sorted(
            Leg.Make(WAW, POZ, 16, 12, 18, 30, scheduleId: 100, orderId: 1, carrier: "IC"),
            Leg.Make(POZ, GLO, 18, 40, 22, 15, scheduleId: 200, orderId: 1, carrier: "KD"),
            Leg.Make(WAW, WRO, 16, 44, 20, 13, scheduleId: 300, orderId: 1, carrier: "IC"),
            Leg.Make(WRO, GLO, 20, 24, 22, 15, scheduleId: 400, orderId: 1, carrier: "Os"));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, GLO, Leg.At(16, 0));

        Assert.Contains(result, j =>
            j.Transfers == 1 &&
            j.Departure == Leg.At(16, 12) &&
            j.Arrival   == Leg.At(22, 15));
    }

    [Fact]
    public void GlogowScenario_TwoTransferRouteDominated_NotReturned()
    {
        // Same arrival time, more transfers → should be pruned
        var legs = Leg.Sorted(
            Leg.Make(WAW, POZ, 16, 12, 18, 30, scheduleId: 100, orderId: 1, carrier: "IC"),
            Leg.Make(POZ, GLO, 18, 40, 22, 15, scheduleId: 200, orderId: 1, carrier: "KD"),
            Leg.Make(WAW, WRO, 16, 44, 20, 13, scheduleId: 300, orderId: 1, carrier: "IC"),
            Leg.Make(WRO, GLO, 20, 24, 22, 15, scheduleId: 400, orderId: 1, carrier: "Os"));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, GLO, Leg.At(16, 0));

        Assert.DoesNotContain(result, j =>
            j.Transfers == 2 &&
            j.Arrival   == Leg.At(22, 15));
    }

    // =========================================================================
    // Pareto set — multiple non-dominated journeys all returned
    // =========================================================================

    [Fact]
    public void ThreeNonDominatedJourneys_AllReturned()
    {
        // A: 2 transfers, arrives 09:00 — fastest
        // B: 1 transfer,  arrives 10:00 — middle
        // C: 0 transfers, arrives 11:00 — direct
        var legs = Leg.Sorted(
            Leg.Make(WAW, MID, 6,  0, 7,  0, scheduleId: 1, orderId: 1),
            Leg.Make(MID, POZ, 7, 10, 8,  0, scheduleId: 2, orderId: 1),
            Leg.Make(POZ, KRK, 8, 10, 9,  0, scheduleId: 3, orderId: 1),
            Leg.Make(WAW, POZ, 7,  0, 8, 30, scheduleId: 4, orderId: 1),
            Leg.Make(POZ, KRK, 8, 40, 10, 0, scheduleId: 5, orderId: 1),
            Leg.Make(WAW, KRK, 8,  0, 11, 0, scheduleId: 6, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(6, 0));

        Assert.Contains(result, j => j.Transfers == 2 && j.Arrival == Leg.At(9,  0));
        Assert.Contains(result, j => j.Transfers == 1 && j.Arrival == Leg.At(10, 0));
        Assert.Contains(result, j => j.Transfers == 0 && j.Arrival == Leg.At(11, 0));
    }

    // =========================================================================
    // Overnight trains
    // =========================================================================

    [Fact]
    public void OvernightTrain_ArrivalNextDay_Found()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, KRK, 22, 8, 1, 7, arrDay: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(22, 0));

        Assert.Single(result);
        Assert.Equal(0, result[0].Transfers);
        Assert.Equal(Leg.At(1, 7, day: 1), result[0].Arrival);
    }

    [Fact]
    public void OvernightConnection_TransferAfterMidnight_Found()
    {
        var legs = Leg.Sorted(
            Leg.Make(WAW, MID, 22,  0, 0, 30, arrDay: 1, scheduleId: 1, orderId: 1),
            Leg.Make(MID, KRK,  0, 45, 3,  0, depDay: 1, arrDay: 1, scheduleId: 2, orderId: 1));

        var result = CsaAlgorithm.FindJourneys(legs, WAW, KRK, Leg.At(22, 0));

        Assert.Single(result);
        Assert.Equal(1, result[0].Transfers);
        Assert.Equal(Leg.At(3, 0, day: 1), result[0].Arrival);
    }
}

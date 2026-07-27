using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Pure Connection Scanning Algorithm implementation.
/// No I/O, no logging, no DI — takes sorted TrainLegs, returns a MultiSegmentTrip.
/// All times are DateTime so overnight trains compare and sort correctly.
/// </summary>
internal static class CsaAlgorithm
{
    private sealed class Label
    {
        public DateTime Arrival { get; init; }
        public JourneySegment? LastLeg { get; init; }
    }

    /// <summary>
    /// Runs a single CSA scan from a given earliest departure DateTime at the origin,
    /// returning the earliest-arrival MultiSegmentTrip to the destination, or null if unreachable.
    /// </summary>
    public static Journey? FindEarliestArrival(
        List<JourneySegment> sortedLegs,
        int fromStationId,
        int toStationId,
        DateTime earliestDeparture)
    {
        var labels = new Dictionary<int, Label>();
        var predecessors = new Dictionary<JourneySegment, JourneySegment?>();

        labels[fromStationId] = new Label { Arrival = earliestDeparture, LastLeg = null };

        foreach (var leg in sortedLegs)
        {
            if (!labels.TryGetValue(leg.FromStationId, out var label))
                continue;

            if (label.Arrival > leg.Departure)
                continue;

            if (labels.TryGetValue(leg.ToStationId, out var current) && leg.Arrival >= current.Arrival)
                continue;

            predecessors[leg] = label.LastLeg;

            labels[leg.ToStationId] = new Label { Arrival = leg.Arrival, LastLeg = leg };

            if (leg.ToStationId == toStationId)
                return Reconstruct(leg, predecessors);
        }

        return null;
    }

    private static Journey Reconstruct(JourneySegment final, Dictionary<JourneySegment, JourneySegment?> predecessors)
    {
        var path = new List<JourneySegment>();
        for (JourneySegment? c = final; c != null; c = predecessors.TryGetValue(c, out var prev) ? prev : null)
            path.Add(c);
        path.Reverse();

        var transfers = 0;
        for (var i = 1; i < path.Count; i++)
            if (path[i].ScheduleId != path[i - 1].ScheduleId || path[i].OrderId != path[i - 1].OrderId)
                transfers++;

        var duration = path[^1].Arrival - path[0].Departure;
        return new Journey(path, transfers, duration);
    }
}

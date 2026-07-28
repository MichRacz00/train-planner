using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Pure Connection Scanning Algorithm implementation.
/// No I/O, no logging, no DI — takes sorted JourneySegments, returns all non-dominated Journeys.
/// All times are DateTime so overnight trains compare and sort correctly.
/// </summary>
internal static class CsaAlgorithm
{
    private sealed class Label
    {
        public DateTime Arrival { get; init; }
        public JourneySegment? LastLeg { get; init; }
        public Label? Previous { get; init; }
        public HashSet<int> VisitedStations { get; init; } = [];
    }

    /// <summary>
    /// Runs a multi-label CSA scan from an earliest departure time,
    /// returning all non-dominated journeys to the destination.
    /// </summary>
    public static IReadOnlyList<Journey> FindJourneys(
        List<JourneySegment> sortedLegs,
        int fromStationId,
        int toStationId,
        DateTime earliestDeparture)
    {
        var labels = new Dictionary<int, List<Label>>();

        labels[fromStationId] =
        [
            new Label
            {
                Arrival = earliestDeparture,
                VisitedStations = [fromStationId]
            }
        ];

        foreach (var leg in sortedLegs)
        {
            if (!labels.TryGetValue(leg.FromStationId, out var stationLabels))
                continue;

            foreach (var label in stationLabels.ToList())
            {
                if (label.Arrival > leg.Departure)
                    continue;
                
                if (label.VisitedStations.Contains(leg.ToStationId)) 
                    continue;
                
                var candidate = new Label
                {
                    Arrival = leg.Arrival,
                    LastLeg = leg,
                    Previous = label,
                    VisitedStations = [.. label.VisitedStations, leg.ToStationId]
                };

                if (!labels.TryGetValue(leg.ToStationId, out var destinationLabels))
                {
                    destinationLabels = [];
                    labels[leg.ToStationId] = destinationLabels;
                }

                if (destinationLabels.Any(existing => Dominates(existing, candidate)))
                    continue;

                destinationLabels.RemoveAll(existing => Dominates(candidate, existing));
                destinationLabels.Add(candidate);
            }
        }

        if (!labels.TryGetValue(toStationId, out var finalLabels))
            return [];

        return finalLabels
            .Select(Reconstruct)
            .ToList();
    }

    private static Journey Reconstruct(Label final)
    {
        var path = new List<JourneySegment>();

        for (var label = final; label?.LastLeg != null; label = label.Previous)
            path.Add(label.LastLeg);

        path.Reverse();

        var transfers = 0;

        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].ScheduleId != path[i - 1].ScheduleId ||
                path[i].OrderId != path[i - 1].OrderId)
            {
                transfers++;
            }
        }

        var duration = path[^1].Arrival - path[0].Departure;

        return new Journey(path, transfers, duration);
    }

    private static bool Dominates(Label existing, Label candidate)
    {
        if (existing.LastLeg?.Category != candidate.LastLeg?.Category)
            return false;

        return existing.Arrival <= candidate.Arrival;
    }
}
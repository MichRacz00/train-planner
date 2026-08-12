using System.Collections.Immutable;
using TrainPlanner.Models;

namespace TrainPlanner.Services;

/// <summary>
/// Pure Connection Scanning Algorithm implementation.
/// No I/O, no logging, no DI — takes sorted JourneySegments, returns all non-dominated Journeys.
/// All times are DateTime so overnight trains compare and sort correctly.
/// </summary>
internal static class CsaAlgorithm
{
    private static readonly int MinimumTransferTimeMinutes = 0;
    
    private sealed class Label
    {
        public DateTime Arrival { get; init; }
        public JourneySegment? LastLeg { get; init; }
        public Label? Previous { get; init; }
        public int TransferCount { get; init; }
        public HashSet<int> VisitedStations { get; init; } = [];
        public DateTime Departure { get; init; }
        public ImmutableArray<JourneySegment> ScheduleSequence { get; init; } = []; //TODO: possibly merge LastLeg into this
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
            // TODO add category sequence
            new Label
            {
                Arrival = earliestDeparture,
                VisitedStations = [fromStationId],
            }
        ];
        
        foreach (var leg in sortedLegs)
        {
            if (!labels.TryGetValue(leg.FromStationId, out var stationLabels))
                continue;

            foreach (var label in stationLabels.ToList())
            {
                var isTransfer = label.LastLeg != null &&
                                 (leg.ScheduleId != label.LastLeg.ScheduleId ||
                                  leg.OrderId    != label.LastLeg.OrderId);

                var departureAfterTransfer = isTransfer
                    ? label.Arrival.AddMinutes(MinimumTransferTimeMinutes)
                    : label.Arrival;
                if (departureAfterTransfer > leg.Departure)
                    continue;
                
                if (label.VisitedStations.Contains(leg.ToStationId)) 
                    continue;
                
                var candidate = new Label
                {
                    Arrival = leg.Arrival,
                    TransferCount = label.TransferCount + (isTransfer ? 1 : 0),
                    LastLeg = leg,
                    Previous = label,
                    VisitedStations = [.. label.VisitedStations, leg.ToStationId],
                    Departure = label.LastLeg == null ? leg.Departure : label.Departure,
                    ScheduleSequence = label.LastLeg is null
                        ? [leg]
                        : isTransfer
                            ? label.ScheduleSequence.Add(leg)
                            : label.ScheduleSequence
                    /*
                    ScheduleSequence = label.LastLeg is null
                        ? [(leg.ScheduleId, leg.OrderId)]
                        : isTransfer
                            ? label.ScheduleSequence.Add((leg.ScheduleId, leg.OrderId))
                            : label.ScheduleSequence
                            */
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

        return new Journey(path, final.TransferCount, duration);
    }

    private static bool Dominates(Label existing, Label candidate)
    {
        // TODO comapre seuqence of train categories
        if (existing.Arrival > candidate.Arrival) return false;
        if (existing.TransferCount > candidate.TransferCount) return false;
        //if (existing.LastLeg?.Category != candidate.LastLeg?.Category) return false;
        
        var existingCategories = existing.ScheduleSequence
            .Select(x => x.Category);

        var candidateCategories = candidate.ScheduleSequence
            .Select(x => x.Category);
        
        if (existing.TransferCount == candidate.TransferCount &&
            !existingCategories.SequenceEqual(candidateCategories))
        {
            return false;
        }
        
        if (existing.LastLeg?.ToStationId == 27805 &&
            existing.LastLeg.Arrival.TimeOfDay > new TimeSpan(20, 0, 0) &&
            existing.LastLeg.Arrival.TimeOfDay < new TimeSpan(21, 0, 0))
        {
            Console.WriteLine(
                $"{string.Join(" -> ", existing.ScheduleSequence)} dominates " +
                $"{string.Join(" -> ", candidate.ScheduleSequence)}");
            Console.WriteLine();
        }
        
        return true;
    }
}
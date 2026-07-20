using TrainPlanner.Services;
using Connection = TrainPlanner.Services.ConnectionBuilder.Connection;

namespace TrainPlanner.Services;

/// <summary>
/// Pure Connection Scanning Algorithm implementation.
/// No I/O, no logging, no DI — takes sorted connections, returns journeys.
/// </summary>
internal static class CsaAlgorithm
{
    internal sealed record Journey(List<Connection> Connections, int Transfers);

    private sealed class Label
    {
        public TimeSpan Arrival { get; init; }
        public Connection? LastConnection { get; init; }
    }

    /// <summary>
    /// Runs a single CSA scan from a given earliest departure time at the origin,
    /// returning the earliest-arrival journey to the destination, or null if unreachable.
    /// </summary>
    public static Journey? FindEarliestArrival(
        List<Connection> sortedConnections,
        int fromStationId,
        int toStationId,
        TimeSpan earliestDeparture)
    {
        var labels = new Dictionary<int, Label>();
        var predecessors = new Dictionary<Connection, Connection?>();

        labels[fromStationId] = new Label
        {
            Arrival = earliestDeparture,
            LastConnection = null
        };

        foreach (var conn in sortedConnections)
        {
            if (!labels.TryGetValue(conn.FromStationId, out var label))
                continue;

            if (label.Arrival > conn.DepartureTime)
                continue;

            if (labels.TryGetValue(conn.ToStationId, out var current) && conn.ArrivalTime >= current.Arrival)
                continue;

            predecessors[conn] = label.LastConnection;

            labels[conn.ToStationId] = new Label
            {
                Arrival = conn.ArrivalTime,
                LastConnection = conn
            };

            if (conn.ToStationId == toStationId)
                return ReconstructJourney(conn, predecessors);
        }

        return null;
    }

    private static Journey ReconstructJourney(
        Connection finalConnection,
        Dictionary<Connection, Connection?> predecessors)
    {
        var path = new List<Connection>();

        for (Connection? c = finalConnection; c != null; c = predecessors.TryGetValue(c, out var prev) ? prev : null)
            path.Add(c);

        path.Reverse();

        var transfers = 0;
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].ScheduleId != path[i - 1].ScheduleId ||
                path[i].OrderId != path[i - 1].OrderId)
                transfers++;
        }

        return new Journey(path, transfers);
    }
}

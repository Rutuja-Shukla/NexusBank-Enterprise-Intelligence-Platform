using System.Globalization;

namespace NexusBank.Application.Common;

public readonly record struct TimeBucket(DateTimeOffset Start, DateTimeOffset End, string Label);

/// <summary>
/// Buckets are produced in memory rather than in SQL: SQLite (used for tests and
/// local development) cannot translate the date functions SQL Server can, and the
/// windows involved are small.
/// </summary>
public static class TimeBuckets
{
    public static IReadOnlyList<TimeBucket> Hourly(DateTimeOffset now, int hours)
    {
        var end = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        return Enumerable.Range(0, hours).Select(offset =>
        {
            var start = end.AddHours(-(hours - offset));
            return new TimeBucket(start, start.AddHours(1), start.ToString("HH:mm", CultureInfo.InvariantCulture));
        }).ToList();
    }

    public static IReadOnlyList<TimeBucket> Daily(DateTimeOffset now, int days)
    {
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, days).Select(offset =>
        {
            var start = today.AddDays(-(days - 1 - offset));
            return new TimeBucket(start, start.AddDays(1), start.ToString("dd MMM", CultureInfo.InvariantCulture));
        }).ToList();
    }

    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (percentile / 100.0) * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high) return sorted[low];
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}

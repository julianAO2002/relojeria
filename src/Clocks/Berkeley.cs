namespace Clocks;

/// <summary>Difference between a participant's clock and the coordinator's clock (null = did not answer).</summary>
public sealed record BerkeleyReading(string Name, TimeSpan? Difference);

public sealed record BerkeleyAdjustment(string Name, TimeSpan? Difference, bool Included, TimeSpan? Adjustment);

public sealed record BerkeleyRound(TimeSpan Average, IReadOnlyList<BerkeleyAdjustment> Adjustments);

public static class Berkeley
{
    /// <summary>
    /// Computes the average of the differences (the coordinator's own reading must be included with
    /// difference 0) and the adjustment each participant must apply: average - difference.
    /// With <paramref name="outlierThreshold"/> > 0 it uses a fault-tolerant average: readings that
    /// differ from the median by more than the threshold are left out of the average, but they are
    /// still told how to adjust. Participants that did not answer get no adjustment.
    /// </summary>
    public static BerkeleyRound Compute(IReadOnlyList<BerkeleyReading> readings, TimeSpan outlierThreshold)
    {
        var answered = readings.Where(r => r.Difference.HasValue).ToList();
        if (answered.Count == 0)
        {
            throw new ArgumentException("At least the coordinator reading is required", nameof(readings));
        }

        var sorted = answered.Select(r => r.Difference!.Value.Ticks).Order().ToList();
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

        bool IsIncluded(BerkeleyReading r) =>
            r.Difference.HasValue &&
            (outlierThreshold <= TimeSpan.Zero || Math.Abs(r.Difference.Value.Ticks - median) <= outlierThreshold.Ticks);

        var included = readings.Where(IsIncluded).ToList();
        var average = TimeSpan.FromTicks((long)included.Average(r => r.Difference!.Value.Ticks));

        var adjustments = readings
            .Select(r => new BerkeleyAdjustment(r.Name, r.Difference, IsIncluded(r),
                r.Difference.HasValue ? average - r.Difference.Value : null))
            .ToList();
        return new BerkeleyRound(average, adjustments);
    }
}

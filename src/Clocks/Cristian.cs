namespace Clocks;

/// <summary>One request to the time server: local send/receive instants, server time and RTT.</summary>
public sealed record CristianSample(DateTime LocalSend, DateTime LocalReceive, DateTime ServerTime, TimeSpan Rtt)
{
    /// <summary>Cristian: the server read its clock, on average, RTT/2 before the reply arrived.</summary>
    public DateTime EstimatedNow => ServerTime + Rtt / 2;

    /// <summary>Correction to add to the local clock.</summary>
    public TimeSpan Offset => EstimatedNow - LocalReceive;

    /// <summary>Worst-case error of the estimate: the server time was read somewhere inside the RTT.</summary>
    public TimeSpan Accuracy => Rtt / 2;
}

public static class Cristian
{
    /// <summary>
    /// Picks the sample with the lowest RTT among those below <paramref name="maxRtt"/>
    /// (the most precise one). Returns null when every sample was discarded.
    /// </summary>
    public static CristianSample? BestSample(IEnumerable<CristianSample> samples, TimeSpan maxRtt) =>
        samples.Where(s => s.Rtt <= maxRtt).OrderBy(s => s.Rtt).FirstOrDefault();
}

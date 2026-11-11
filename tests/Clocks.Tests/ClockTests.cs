using Clocks;
using Xunit;

namespace Clocks.Tests;

public class SimulatedClockTests
{
    private static readonly DateTime Start = new(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Shows_the_case_study_times_at_the_scenario_start()
    {
        var real = Start;
        var serverB = new SimulatedClock(TimeSpan.FromSeconds(8), 0, Start, null, () => real);
        var serverC = new SimulatedClock(TimeSpan.FromSeconds(-5), 0, Start, null, () => real);

        Assert.Equal("10:00:08.000", TimeFormat.Time(serverB.Now));
        Assert.Equal("09:59:55.000", TimeFormat.Time(serverC.Now));
    }

    [Fact]
    public void Drift_accumulates_and_adjust_changes_only_the_application_clock()
    {
        var real = Start;
        var clock = new SimulatedClock(TimeSpan.Zero, driftPpm: 1000, Start, null, () => real);

        real = Start.AddSeconds(10); // 1000 ppm = 1 ms per second
        Assert.Equal("10:00:10.010", TimeFormat.Time(clock.Now));

        clock.Adjust(TimeSpan.FromMilliseconds(-10));
        Assert.Equal("10:00:10.000", TimeFormat.Time(clock.Now));
        Assert.Equal(TimeSpan.FromMilliseconds(-10), clock.Correction);
    }
}

public class CristianTests
{
    private static readonly DateTime T = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Estimates_server_time_plus_half_the_round_trip()
    {
        // Client A (10:00:00) asks the server (10:00:02); RTT 100 ms.
        var sample = new CristianSample(T, T.AddMilliseconds(100), T.AddSeconds(2).AddMilliseconds(50),
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(T.AddSeconds(2).AddMilliseconds(100), sample.EstimatedNow);
        Assert.Equal(TimeSpan.FromSeconds(2), sample.Offset);
        Assert.Equal(TimeSpan.FromMilliseconds(50), sample.Accuracy);
    }

    [Fact]
    public void Best_sample_is_the_lowest_rtt_and_slow_samples_are_discarded()
    {
        CristianSample S(int rttMs) => new(T, T, T, TimeSpan.FromMilliseconds(rttMs));
        var samples = new[] { S(40), S(12), S(3000), S(25) };

        Assert.Equal(TimeSpan.FromMilliseconds(12), Cristian.BestSample(samples, TimeSpan.FromSeconds(1))!.Rtt);
        Assert.Null(Cristian.BestSample(new[] { S(3000), S(2500) }, TimeSpan.FromSeconds(1)));
    }
}

public class BerkeleyTests
{
    private static BerkeleyReading R(string name, double? seconds) =>
        new(name, seconds is null ? null : TimeSpan.FromSeconds(seconds.Value));

    [Fact]
    public void Everyone_moves_to_the_average_including_the_coordinator()
    {
        var round = Berkeley.Compute(new[] { R("coord", 0), R("n1", 10), R("n2", -6), R("n3", 4) }, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(2), round.Average);
        Assert.Equal(new double?[] { 2, -8, 8, -2 },
            round.Adjustments.Select(a => a.Adjustment?.TotalSeconds).ToArray());
    }

    [Fact]
    public void A_five_minute_outlier_drags_the_plain_average()
    {
        var round = Berkeley.Compute(new[] { R("coord", 0), R("n1", 5), R("n2", -3), R("n3", 300) }, TimeSpan.Zero);

        Assert.Equal(75.5, round.Average.TotalSeconds);
    }

    [Fact]
    public void Fault_tolerant_average_ignores_the_outlier_but_still_corrects_it()
    {
        var round = Berkeley.Compute(new[] { R("coord", 0), R("n1", 5), R("n2", -3), R("n3", 300) },
            TimeSpan.FromSeconds(30));

        Assert.Equal(2.0 / 3, round.Average.TotalSeconds, precision: 6);
        var outlier = round.Adjustments.Single(a => a.Name == "n3");
        Assert.False(outlier.Included);
        Assert.Equal(2.0 / 3 - 300, outlier.Adjustment!.Value.TotalSeconds, precision: 6);
    }

    [Fact]
    public void Disconnected_participants_are_left_out_and_get_no_adjustment()
    {
        var round = Berkeley.Compute(new[] { R("coord", 0), R("n1", 6), R("n2", null) }, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(3), round.Average);
        Assert.Null(round.Adjustments.Single(a => a.Name == "n2").Adjustment);
    }
}

public class LogicalClockTests
{
    [Fact]
    public void Lamport_ticks_on_local_events_and_takes_the_max_on_receive()
    {
        var orders = new LamportClock();
        var inventory = new LamportClock();
        var payments = new LamportClock(initial: 10);

        Assert.Equal(1, orders.Tick());                // order received
        var sent = orders.Tick();                       // message to inventory carries 2
        Assert.Equal(3, inventory.Receive(sent));       // higher than its own 0 -> jumps to 3
        Assert.Equal(11, payments.Receive(sent));       // lower than its own 10 -> 11
    }

    [Fact]
    public void Vector_clocks_detect_concurrency_that_lamport_cannot()
    {
        var all = new[] { "I", "O", "P" };
        var orders = new VectorClock("O", all);
        var payments = new VectorClock("P", all);

        var a = orders.Tick();                          // O=1           Lamport 1
        payments.Tick();
        var b = payments.Tick();                        // P=2           Lamport 2
        var c = payments.Receive(orders.Tick());        // after O sends Lamport 3

        Assert.True(VectorClock.Concurrent(a, b));      // L(a)=1 < L(b)=2, yet concurrent
        Assert.True(VectorClock.HappenedBefore(a, c));
        Assert.True(VectorClock.HappenedBefore(b, c));
    }
}

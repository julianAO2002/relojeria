using System.Globalization;

namespace Clocks;

public static class TimeFormat
{
    public static string Time(DateTime value) => value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Signed duration in seconds with millisecond precision, e.g. "+8.004 s".</summary>
    public static string Delta(TimeSpan value) =>
        (value >= TimeSpan.Zero ? "+" : "-") + Math.Abs(value.TotalSeconds).ToString("0.000", CultureInfo.InvariantCulture) + " s";

    public static string Ms(TimeSpan value) =>
        value.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    public static string SignedMs(TimeSpan value) =>
        (value >= TimeSpan.Zero ? "+" : "-") + Math.Abs(value.TotalMilliseconds).ToString("0.0", CultureInfo.InvariantCulture) + " ms";
}

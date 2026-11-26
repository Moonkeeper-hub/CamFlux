using System.Globalization;

namespace CamFlux.Core;

public sealed record StreamProgress(long? Frame = null, double? Fps = null, double? BitrateKbps = null,
    double? Speed = null, long? DuplicateFrames = null, long? DroppedFrames = null);

public sealed class ProgressParser
{
    private StreamProgress current = new();

    public StreamProgress? Read(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0) return null;
        var value = line[(separator + 1)..].Trim();
        switch (line[..separator])
        {
            case "frame": current = current with { Frame = Count(value) }; break;
            case "fps": current = current with { Fps = Number(value) }; break;
            case "bitrate": current = current with { BitrateKbps = Unit(value, "kbits/s") }; break;
            case "speed": current = current with { Speed = Unit(value, "x") }; break;
            case "dup_frames": current = current with { DuplicateFrames = Count(value) }; break;
            case "drop_frames": current = current with { DroppedFrames = Count(value) }; break;
            case "progress" when value is "continue" or "end":
                var completed = current;
                current = new();
                return completed;
        }
        return null;
    }

    private static long? Count(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= 0 ? value : null;
    private static double? Unit(string text, string unit) =>
        text.EndsWith(unit, StringComparison.Ordinal) ? Number(text[..^unit.Length]) : null;
}

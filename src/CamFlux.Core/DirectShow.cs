using System.Globalization;
using System.Text.RegularExpressions;

namespace CamFlux.Core;

public static class DirectShow
{
    public static IReadOnlyList<Device> Devices(string stderr)
    {
        var devices = new List<Device>();
        var lines = stderr.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var device = Regex.Match(lines[index], "\"(.*)\" \\((video(?:, audio)?|audio(?:, video)?)\\)");
            if (!device.Success)
                continue;
            var name = device.Groups[1].Value;
            var types = device.Groups[2].Value.Split(", ");
            var alternativeId = index + 1 < lines.Length
                ? Regex.Match(lines[index + 1], "Alternative name \"(.*)\"").Groups[1].Value : "";
            foreach (var type in types)
                devices.Add(new Device(name, alternativeId.Length == 0 ? name : alternativeId, type == "audio"));
        }
        var duplicates = devices.GroupBy(d => (d.Name, d.IsAudio)).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet();
        return devices.DistinctBy(d => (d.Id, d.IsAudio)).Select(d => duplicates.Contains((d.Name, d.IsAudio))
            ? d with { Name = $"{d.Name} [{d.Id}]" } : d).ToArray();
    }

    public static IReadOnlyList<CaptureMode> Modes(string stderr)
    {
        var modes = new List<CaptureMode>();
        foreach (var line in stderr.Split('\n'))
        {
            var format = Regex.Match(line, @"(pixel_format|vcodec)=(\w+)");
            var min = Regex.Match(line, @"min s=(\d+)x(\d+) fps=([0-9.]+)");
            var max = Regex.Match(line, @"max s=(\d+)x(\d+) fps=([0-9.]+)");
            if (!format.Success || !min.Success || !max.Success)
                continue;

            Add(min);
            Add(max);
            // A range for a fixed resolution is evidence for candidates, not proof of driver support.
            if (min.Groups[1].Value == max.Groups[1].Value && min.Groups[2].Value == max.Groups[2].Value &&
                int.TryParse(min.Groups[1].Value, out var width) && int.TryParse(min.Groups[2].Value, out var height) &&
                width > 0 && height > 0 &&
                double.TryParse(min.Groups[3].Value, CultureInfo.InvariantCulture, out var lower) &&
                double.TryParse(max.Groups[3].Value, CultureInfo.InvariantCulture, out var upper))
            {
                foreach (var fps in new[] { 15, 20, 25, 30 })
                {
                    if (fps > lower && fps < upper)
                        modes.Add(new CaptureMode(width, height, fps.ToString(CultureInfo.InvariantCulture),
                            format.Groups[2].Value, format.Groups[1].Value == "vcodec", IsInferred: true));
                }
            }

            void Add(Match match)
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var width) &&
                    int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var height) &&
                    double.TryParse(match.Groups[3].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fps) &&
                    width > 0 && height > 0 && fps is > 0 and <= 1000)
                    modes.Add(new CaptureMode(width, height, match.Groups[3].Value,
                        format.Groups[2].Value, format.Groups[1].Value == "vcodec"));
            }
        }
        return modes.OrderBy(m => m.IsInferred).DistinctBy(m => (m.Width, m.Height,
                double.Parse(m.Fps, CultureInfo.InvariantCulture), m.Format, m.IsCodec)).OrderByDescending(m => (long)m.Width * m.Height)
            .ThenByDescending(m => double.Parse(m.Fps, CultureInfo.InvariantCulture)).ToArray();
    }
}

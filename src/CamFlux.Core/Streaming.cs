using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CamFlux.Core;

public sealed record Endpoint(string Host = "", int Port = 8890, int LatencyMs = 500);
public sealed record Credentials(string User = "", string Password = "");
public sealed record Device(string Name, string Id, bool IsAudio)
{
    public override string ToString() => Name;
}
public sealed record CaptureMode(int Width, int Height, string Fps, string Format, bool IsCodec)
{
    public override string ToString() => $"{Width} × {Height}  ·  {Fps} fps  ·  {Format}";
}
public sealed record Camera(string DeviceId, string Name, bool Enabled, string Path, CaptureMode? Mode,
    int Bitrate, string? AudioId);
public sealed record Profile(string Name, Endpoint Endpoint, Camera[] Cameras, string Language, string? FfmpegPath);

public static class Streaming
{
    public const int AudioKbps = 128;
    public static double EstimatedMbps(IEnumerable<Camera> cameras) => cameras.Where(c => c.Enabled)
        .Sum(c => (double)Math.Max(0, c.Bitrate) + (c.AudioId is null or "" ? 0 : AudioKbps)) * 1.30 / 1000;

    public static string Url(Endpoint endpoint, string path, Credentials credentials, bool publish)
    {
        var host = endpoint.Host.Trim();
        if (host.StartsWith('[') && host.EndsWith(']'))
            host = host[1..^1];
        var hostType = Uri.CheckHostName(host);
        if (hostType == UriHostNameType.Unknown || host.Contains('%') ||
            endpoint.Port is < 1 or > 65535 || endpoint.LatencyMs is < 1 or > 60000)
            throw new ArgumentException("InvalidEndpoint");
        if (!Regex.IsMatch(path, @"\A[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*\z"))
            throw new ArgumentException("InvalidPath");

        var fields = new[] { credentials.User, credentials.Password };
        if (fields.Any(s => !Regex.IsMatch(s, @"\A[A-Za-z0-9._~-]*\z")) ||
            (fields[0].Length == 0) != (fields[1].Length == 0))
            throw new ArgumentException("InvalidCredentials");

        var streamId = $"{(publish ? "publish" : "read")}:{path}";
        if (credentials.User.Length > 0)
            streamId += $":{credentials.User}:{credentials.Password}";
        if (System.Text.Encoding.UTF8.GetByteCount(streamId) > 512)
            throw new ArgumentException("InvalidCredentials");
        if (hostType == UriHostNameType.IPv6)
            host = $"[{IPAddress.Parse(host)}]";
        return FormattableString.Invariant($"srt://{host}:{endpoint.Port}?mode=caller&latency={endpoint.LatencyMs * 1000}&streamid={streamId}") +
            (publish ? "&pkt_size=1316" : "");
    }

    public static string[] Arguments(Camera camera, Endpoint endpoint, Credentials credentials)
    {
        var mode = camera.Mode;
        if (mode is null || mode.Width <= 0 || mode.Height <= 0 ||
            !double.TryParse(mode.Fps, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fps) ||
            fps is <= 0 or > 1000 || !Regex.IsMatch(mode.Format, @"\A[a-zA-Z0-9_]+\z") ||
            camera.Bitrate is < 100 or > 100000 || !ValidDevice(camera.DeviceId) ||
            (camera.AudioId is not null && !ValidDevice(camera.AudioId)))
            throw new ArgumentException("InvalidCapture");

        var size = FormattableString.Invariant($"{mode.Width}x{mode.Height}");
        var bitrate = FormattableString.Invariant($"{camera.Bitrate}k");
        var gop = ((int)Math.Round(fps * 2)).ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-progress", "pipe:1",
            "-f", "dshow", "-rtbufsize", "64M", "-video_size", size, "-framerate", mode.Fps,
            mode.IsCodec ? "-vcodec" : "-pixel_format", mode.Format,
            "-i", "video=" + camera.DeviceId + (camera.AudioId is null ? "" : ":audio=" + camera.AudioId),
            "-map", "0:v:0", "-c:v", "libx264", "-preset", "veryfast", "-tune", "zerolatency",
            "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p", "-g", gop, "-sc_threshold", "0",
            "-b:v", bitrate, "-maxrate", bitrate, "-bufsize", FormattableString.Invariant($"{camera.Bitrate * 2}k") };
        if (camera.AudioId is null)
            args.Add("-an");
        else
            args.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", FormattableString.Invariant($"{AudioKbps}k")]);
        args.AddRange(["-f", "mpegts", Url(endpoint, camera.Path, credentials, true)]);
        return [.. args];
    }

    private static bool ValidDevice(string id) => !string.IsNullOrWhiteSpace(id) &&
        !id.Contains(':') && !id.Any(char.IsControl);
}

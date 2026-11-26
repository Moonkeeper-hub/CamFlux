using CamFlux.Core;
using CamFlux.Infrastructure;

namespace CamFlux.Tests;

public sealed class ReviewRegressionTests
{
    private static Camera Camera(string id, string path, string? audio = null) =>
        new(id, id, true, path, new CaptureMode(640, 480, "30", "yuyv422", false), 2500, audio);

    [Fact]
    public void InvalidRowsDoNotReserveOwnershipOrBlockIndependentRows()
    {
        Camera[] requested =
        [
            Camera("A", "shared") with { Mode = null },
            Camera("B", "shared", "unplugged"),
            Camera("absent", "shared"),
            Camera("C", "shared", "mic"),
            Camera("D", "shared"),
            Camera("C", "other"),
            Camera("E", "other", "mic2"),
            Camera("F", "other")
        ];
        var plan = StartPlanning.Plan(requested, [], new HashSet<string> { "A", "B", "C", "D", "E", "F" },
            new HashSet<string> { "mic", "mic2" }, new Endpoint("example.invalid"), new()).ToArray();

        Assert.Equal(["InvalidCapture", "MissingAudio", "Disconnected", null,
            "DuplicateStream", "DuplicateStream", "DuplicateAudio", null], plan.Select(p => p.Error));
        Assert.Equal(["C", "F"], plan.Where(p => p.Arguments is not null).Select(p => p.Camera.DeviceId));
    }

    [Fact]
    public void ExistingOwnersKeepDevicePathAndAudioWhenStartingMoreStreams()
    {
        var plan = StartPlanning.Plan([Camera("a", "other"), Camera("B", "live"), Camera("C", "stereo", "mic2"),
            Camera("D", "independent")], [Camera("A", "live", "mic")],
            new HashSet<string> { "a", "B", "C", "D" }, new HashSet<string> { "mic", "mic2" },
            new Endpoint("example.invalid"), new()).ToArray();

        Assert.Equal(["DuplicateStream", "DuplicateStream", "DuplicateAudio", null], plan.Select(p => p.Error));
    }

    [Fact]
    public void ProgressParsesWholeBlocksAndDoesNotCarryMissingMetricsForward()
    {
        var parser = new ProgressParser();
        foreach (var line in new[] { "frame= 301", "fps=29.97", "bitrate=1234.5kbits/s", "speed=1.05x",
                     "dup_frames=2", "drop_frames=3", "out_time=N/A" })
            Assert.Null(parser.Read(line));

        Assert.Equal(new StreamProgress(301, 29.97, 1234.5, 1.05, 2, 3), parser.Read("progress=continue"));
        parser.Read("frame=302");
        parser.Read("speed=N/A");
        parser.Read("bitrate=NaNkbits/s");
        parser.Read("drop_frames=-1");
        Assert.Equal(new StreamProgress(Frame: 302), parser.Read("progress=end"));
    }

    [Theory]
    [InlineData("Connection refused: srt://example.invalid?streamid=publish:stage:secret-user:secret-pass", "ConnectionRefused")]
    [InlineData("Error initializing output stream: ffmpeg -headers 'Authorization: secret-pass'", "OutputInitializationFailed")]
    [InlineData("argv: -passphrase=secret-pass -i video=secret-user", "FfmpegWarning")]
    [InlineData("[dshow] Could not set video options: secret-pass", "UnsupportedCaptureMode")]
    [InlineData("Connection timed out: publish%3Astage%3Asecret-user%3Asecret-pass", "Timeout")]
    [InlineData("secret-pass", "FfmpegWarning")]
    public void DiagnosticsExposeOnlySafeNormalizedDetails(string stderr, string expected)
    {
        var diagnostic = FfmpegDiagnostics.Describe(stderr);
        Assert.Equal(expected, diagnostic);
        Assert.DoesNotContain("secret", diagnostic);
        Assert.DoesNotContain("srt://", diagnostic);
        Assert.DoesNotContain("-passphrase", diagnostic);
    }

    [Fact]
    public void ModeCandidatesStayWithinFixedResolutionAndReportedModesTakePrecedence()
    {
        var modes = DirectShow.Modes("""
            pixel_format=yuyv422 min s=640x480 fps=10 max s=640x480 fps=60
            pixel_format=yuyv422 min s=640x480 fps=25.00 max s=640x480 fps=25.00
            vcodec=mjpeg min s=320x240 fps=10 max s=1920x1080 fps=60
            """);

        Assert.Equal(["15", "20", "30"], modes.Where(m => m.IsInferred).Select(m => m.Fps).Order());
        Assert.All(modes.Where(m => m.IsInferred), m => Assert.Equal((640, 480, "yuyv422"), (m.Width, m.Height, m.Format)));
        Assert.False(Assert.Single(modes, m => m.Fps == "25.00").IsInferred);
        Assert.DoesNotContain(modes, m => m.Fps == "25");
    }
}

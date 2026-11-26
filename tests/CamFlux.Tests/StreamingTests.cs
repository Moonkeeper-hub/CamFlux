using CamFlux.Core;

namespace CamFlux.Tests;

public sealed class StreamingTests
{
    private static readonly Endpoint Server = new("example.invalid");
    private static readonly Camera Camera = new("device", "Camera", true, "stage/left",
        new CaptureMode(1280, 720, "29.97", "mjpeg", true), 2500, null);

    [Fact]
    public void PublisherUsesMicrosecondsAndMpegTsPackets()
    {
        Assert.Equal("srt://example.invalid:8890?mode=caller&latency=500000&streamid=publish:stage/left:publisher:example-password&pkt_size=1316",
            Streaming.Url(Server, Camera.Path, new("publisher", "example-password"), publish: true));
    }

    [Fact]
    public void ReaderUsesOnlyReaderCredentialsAndSupportsIpv6()
    {
        Assert.Equal("srt://[2001:db8::1]:8890?mode=caller&latency=500000&streamid=read:stage/left:reader:example-password",
            Streaming.Url(Server with { Host = "2001:db8::1" }, Camera.Path, new("reader", "example-password"), publish: false));
    }

    [Theory]
    [InlineData("bad:field")]
    [InlineData("bad%3Afield")]
    [InlineData("bad&field")]
    [InlineData("bad field")]
    [InlineData("bad\nfield")]
    public void RejectsCredentialsWithAmbiguousCharacters(string field)
    {
        Assert.Throws<ArgumentException>(() => Streaming.Url(Server, Camera.Path, new("reader", field), false));
    }

    [Theory]
    [InlineData("", 8890, 500, "stage")]
    [InlineData("example.invalid?foo", 8890, 500, "stage")]
    [InlineData("example.invalid", 65536, 500, "stage")]
    [InlineData("example.invalid", 8890, 0, "stage")]
    [InlineData("example.invalid", 8890, 500, "stage:other")]
    [InlineData("example.invalid", 8890, 500, "../stage")]
    public void RejectsInvalidEndpointAndPath(string host, int port, int latency, string path)
    {
        Assert.Throws<ArgumentException>(() => Streaming.Url(new(host, port, latency), path, new(), true));
    }

    [Fact]
    public void BuildsInputCodecAndOutputSettingsSeparately()
    {
        var args = Streaming.Arguments(Camera with { AudioId = "microphone" }, Server, new());
        Assert.Equal("mjpeg", args[Array.IndexOf(args, "-vcodec") + 1]);
        Assert.Equal("video=device:audio=microphone", args[Array.IndexOf(args, "-i") + 1]);
        Assert.Equal("libx264", args[Array.IndexOf(args, "-c:v") + 1]);
        Assert.Equal("60", args[Array.IndexOf(args, "-g") + 1]);
        Assert.Equal("aac", args[Array.IndexOf(args, "-c:a") + 1]);
    }

    [Fact]
    public void RejectsMissingModeAndInvalidBitrate()
    {
        Assert.Throws<ArgumentException>(() => Streaming.Arguments(Camera with { Mode = null }, Server, new()));
        Assert.Throws<ArgumentException>(() => Streaming.Arguments(Camera with { Bitrate = -1 }, Server, new()));
    }

    [Fact]
    public void EstimateIncludesOnlyEnabledFeedsAndSelectedAudio()
    {
        Assert.Equal(3.4164, Streaming.EstimatedMbps([Camera with { AudioId = "mic" }, Camera with { Enabled = false }]), 4);
    }
}

using CamFlux.Core;

namespace CamFlux.Tests;

public sealed class DirectShowTests
{
    [Fact]
    public void IdenticalNamesRetainIndependentIdsAndMixedMedia()
    {
        var devices = DirectShow.Devices("""
            [dshow @ 123] "USB Camera" (video)
            [dshow @ 123]   Alternative name "@device_pnp_1"
            [dshow @ 123] "USB Camera" (video, audio)
            [dshow @ 123]   Alternative name "@device_pnp_2"
            [dshow @ 123] Could not open input dummy
            """);

        Assert.Equal(["@device_pnp_1", "@device_pnp_2"], devices.Where(d => !d.IsAudio).Select(d => d.Id));
        Assert.Equal("@device_pnp_2", Assert.Single(devices, d => d.IsAudio).Id);
    }

    [Fact]
    public void ModesIncludeRawAndCompressedFormatsWithFractionalFps()
    {
        var modes = DirectShow.Modes("""
            [dshow @ 123]   pixel_format=yuyv422  min s=640x480 fps=5 max s=640x480 fps=30
            [dshow @ 123]   vcodec=mjpeg  min s=1920x1080 fps=29.97 max s=1920x1080 fps=29.97
            [dshow @ 123]   vcodec=mjpeg  min s=1920x1080 fps=29.97 max s=1920x1080 fps=29.97
            [dshow @ 123] Error opening input: Immediate exit requested
            """);

        Assert.Equal([new(1920, 1080, "29.97", "mjpeg", true), new(640, 480, "30", "yuyv422", false),
            new(640, 480, "25", "yuyv422", false, true), new(640, 480, "20", "yuyv422", false, true), new(640, 480, "15", "yuyv422", false, true), new(640, 480, "5", "yuyv422", false)], modes);
    }
}

namespace CamFlux.Infrastructure;

public static class FfmpegDiagnostics
{
    // Only emit fixed descriptions of recognized FFmpeg errors. Derived text can contain
    // arguments escaped by FFmpeg, truncated URLs, or isolated credential values.
    private static readonly (string Text, string Code)[] Details =
    [
        ("could not set video options", "UnsupportedCaptureMode"),
        ("could not set video format", "UnsupportedCaptureMode"),
        ("could not find video device", "VideoDeviceMissing"),
        ("could not find audio device", "AudioDeviceMissing"),
        ("could not run filter", "CaptureFilterStartFailed"),
        ("real-time buffer", "CaptureBufferFull"),
        ("connection refused", "ConnectionRefused"),
        ("incorrect passphrase", "SrtBadPassphrase"),
        ("authorization failed", "SrtAuthorizationFailed"),
        ("failed to resolve hostname", "HostResolutionFailed"),
        ("network is unreachable", "NetworkUnreachable"),
        ("connection reset by peer", "ConnectionReset"),
        ("invalid argument", "FfmpegInvalidArgument"),
        ("error initializing output stream", "OutputInitializationFailed")
    ];

    public static string Describe(string stderr)
    {
        foreach (var (text, code) in Details)
            if (stderr.Contains(text, StringComparison.OrdinalIgnoreCase))
                return code;
        if (stderr.Contains("reject", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (stderr.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (stderr.Contains("connection", StringComparison.OrdinalIgnoreCase)) return "Connection";
        if (stderr.Contains("[dshow", StringComparison.OrdinalIgnoreCase)) return "Capture";
        if (stderr.Contains("x264", StringComparison.OrdinalIgnoreCase)) return "Encoder";
        return "FfmpegWarning";
    }
}

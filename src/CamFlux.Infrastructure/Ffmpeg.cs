using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CamFlux.Core;

namespace CamFlux.Infrastructure;

public sealed class Ffmpeg(string executable) : IStreamRunner
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private const string Build = "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/autobuild-2026-11-26-12-56/ffmpeg-n8.1.3-41-ga8029b6528-win64-gpl-8.1.zip";
    public const string Sha256 = "dbbd7275987024a34bfad96b3c3cc148a47bea604ff7100705d8f14ab1cabfb8";

    public static async Task<string> ResolveAsync(string directory, string? preferred, Action downloading, CancellationToken token)
    {
        var managed = Path.Combine(directory, "ffmpeg-" + Sha256[..12], "ffmpeg.exe");
        var candidates = new[] { preferred, managed, Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(Path.IsPathFullyQualified).Select(p => Path.Combine(p, "ffmpeg.exe")));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (candidate is null || !File.Exists(candidate))
                continue;
            if (await new Ffmpeg(candidate).SupportsMvpAsync(token).ConfigureAwait(false))
                return candidate;
        }

        downloading();
        using var install = CancellationTokenSource.CreateLinkedTokenSource(token);
        install.CancelAfter(TimeSpan.FromMinutes(10));
        token = install.Token;
        Directory.CreateDirectory(directory);
        var staging = Directory.CreateTempSubdirectory("camflux-ffmpeg-");
        var targetDirectory = Path.GetDirectoryName(managed)!;
        Directory.CreateDirectory(targetDirectory);
        var stagedExe = Path.Combine(targetDirectory, Guid.NewGuid() + ".exe");
        try
        {
            var zipPath = Path.Combine(staging.FullName, "ffmpeg.zip");
            await using (var zip = File.Create(zipPath))
            {
                using var response = await Http.GetAsync(Build, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(zip, token).ConfigureAwait(false);
                zip.Position = 0;
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(zip, token).ConfigureAwait(false));
                if (!digest.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("HashMismatch");
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.Entries.Single(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.Ordinal));
            await using (var source = entry.Open())
            await using (var destination = File.Create(stagedExe))
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            if (!await new Ffmpeg(stagedExe).SupportsMvpAsync(token).ConfigureAwait(false))
                throw new InvalidDataException("InvalidFfmpeg");
            token.ThrowIfCancellationRequested();
            File.Move(stagedExe, managed, true);
            return managed;
        }
        finally
        {
            File.Delete(stagedExe);
            staging.Delete(true);
        }
    }

    public async Task<bool> SupportsMvpAsync(CancellationToken token)
    {
        try
        {
            var protocols = await QueryAsync(["-hide_banner", "-protocols"], token).ConfigureAwait(false);
            var devices = await QueryAsync(["-hide_banner", "-devices"], token).ConfigureAwait(false);
            var encode = await QueryAsync(["-hide_banner", "-f", "lavfi", "-i", "color=size=64x64:rate=1",
                "-frames:v", "1", "-c:v", "libx264", "-f", "null", "-"], token).ConfigureAwait(false);
            return protocols.Code == 0 && Regex.IsMatch(protocols.Output, @"(?m)^\s*srt\s*$") &&
                devices.Code == 0 && Regex.IsMatch(devices.Output, @"(?m)^\s*D\s+dshow\s") && encode.Code == 0;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Device>> DevicesAsync(CancellationToken token)
    {
        var result = await QueryAsync(["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], token)
            .ConfigureAwait(false);
        return DirectShow.Devices(result.Error);
    }

    public async Task<IReadOnlyList<CaptureMode>> ModesAsync(string id, CancellationToken token)
    {
        var result = await QueryAsync(["-hide_banner", "-list_options", "true", "-f", "dshow", "-i", "video=" + id], token)
            .ConfigureAwait(false);
        return DirectShow.Modes(result.Error);
    }

    private async Task<(int Code, string Output, string Error)> QueryAsync(string[] args, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = Start(args);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
        }
        finally
        {
            await TerminateAsync(process).ConfigureAwait(false);
        }
    }

    public async Task<int> RunAsync(string[] arguments, Action<StreamProgress> progress, Action<string> diagnostic, CancellationToken cancellation)
    {
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        watchdog.CancelAfter(TimeSpan.FromSeconds(45));
        var token = watchdog.Token;
        using var process = Start(arguments);
        long lastFrame = 0;
        var parser = new ProgressParser();
        var stdout = DrainAsync(process.StandardOutput, line =>
        {
            if (parser.Read(line) is not { } snapshot) return;
            if (snapshot.Frame is long frame && frame > lastFrame)
            {
                lastFrame = frame;
                watchdog.CancelAfter(TimeSpan.FromSeconds(30));
            }
            progress(snapshot);
        }, token);
        string? lastDiagnostic = null;
        var stderr = DrainAsync(process.StandardError, line =>
        {
            var code = FfmpegDiagnostics.Describe(line);
            if (code == lastDiagnostic) return;
            lastDiagnostic = code;
            diagnostic(code);
        }, token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            diagnostic("Stalled");
            return -1;
        }
        finally
        {
            await TerminateAsync(process).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    private Process Start(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        info.Environment.Remove("FFREPORT");
        info.ArgumentList.Add("-nostdin");
        foreach (var arg in arguments)
            info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("ProcessError");
    }

    private static async Task TerminateAsync(Process process)
    {
        if (process.HasExited)
            return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { }
        await process.WaitForExitAsync().ConfigureAwait(false);
    }

    private static async Task DrainAsync(StreamReader reader, Action<string> handle, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            handle(line);
    }
}

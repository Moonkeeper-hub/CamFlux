namespace CamFlux.Core;

public enum StreamState { Idle, Starting, Running, Retrying, Stopping, Failed }
public interface IStreamRunner
{
    Task<int> RunAsync(string[] arguments, Action<string> progress, Action<string> diagnostic, CancellationToken cancellation);
}

public sealed class Supervisor(IStreamRunner runner, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan StablePeriod = TimeSpan.FromMinutes(1);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    public const int RetryLimit = 5;

    public async Task RunAsync(string[] arguments, Action<StreamState> state, Action<string> progress,
        Action<string> diagnostic, CancellationToken cancellation)
    {
        var failures = 0;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                state(StreamState.Starting);
                long? firstProgress = null;
                try
                {
                    var code = await runner.RunAsync(arguments, value =>
                    {
                        if (firstProgress is null)
                        {
                            firstProgress = System.Diagnostics.Stopwatch.GetTimestamp();
                            state(StreamState.Running);
                        }
                        progress(value);
                    }, diagnostic, cancellation).ConfigureAwait(false);
                    diagnostic($"FFmpeg exit: {code}");
                }
                catch (Exception) when (!cancellation.IsCancellationRequested)
                {
                    diagnostic("ProcessError");
                }

                cancellation.ThrowIfCancellationRequested();
                if (firstProgress is long started && System.Diagnostics.Stopwatch.GetElapsedTime(started) >= StablePeriod)
                    failures = 0;
                if (failures == RetryLimit)
                {
                    state(StreamState.Failed);
                    return;
                }

                state(StreamState.Retrying);
                await wait(TimeSpan.FromSeconds(1 << failures++), cancellation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            state(StreamState.Idle);
        }
    }
}

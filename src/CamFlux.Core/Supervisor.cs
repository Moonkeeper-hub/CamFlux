namespace CamFlux.Core;

public enum StreamState { Idle, Starting, Running, Retrying, Stopping, Failed }
public interface IStreamRunner
{
    Task<int> RunAsync(string[] arguments, Action<StreamProgress> progress, Action<string> diagnostic, CancellationToken cancellation);
}

public sealed class Supervisor(IStreamRunner runner, Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan StablePeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumProgressGap = TimeSpan.FromSeconds(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    public const int RetryLimit = 5;

    public async Task RunAsync(string[] arguments, Action<StreamState> state, Action<StreamProgress> progress,
        Action<string> diagnostic, CancellationToken cancellation)
    {
        var failures = 0;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                state(StreamState.Starting);
                long lastFrame = 0;
                long? advancingSince = null;
                long lastAdvance = 0;
                try
                {
                    var code = await runner.RunAsync(arguments, value =>
                    {
                        if (value.Frame is long frame && frame > lastFrame)
                        {
                            var now = clock.GetTimestamp();
                            if (lastFrame == 0)
                                state(StreamState.Running);
                            if (advancingSince is null || clock.GetElapsedTime(lastAdvance, now) > MaximumProgressGap)
                                advancingSince = now;
                            lastFrame = frame;
                            lastAdvance = now;
                            if (clock.GetElapsedTime(advancingSince.Value, now) >= StablePeriod)
                                failures = 0;
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

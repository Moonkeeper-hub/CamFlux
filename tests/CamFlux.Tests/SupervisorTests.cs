using CamFlux.Core;

namespace CamFlux.Tests;

public sealed class SupervisorTests
{
    private sealed class Runner(Func<Action<StreamProgress>, CancellationToken, Task<int>> run) : IStreamRunner
    {
        public int Calls { get; private set; }
        public Task<int> RunAsync(string[] arguments, Action<StreamProgress> progress, Action<string> diagnostic,
            CancellationToken cancellation)
        {
            Calls++;
            return run(progress, cancellation);
        }
    }

    [Fact]
    public async Task FailuresUseBoundedExponentialBackoff()
    {
        var runner = new Runner((_, _) => Task.FromResult(1));
        var states = new List<StreamState>();
        var delays = new List<double>();
        var supervisor = new Supervisor(runner, (delay, _) =>
        {
            delays.Add(delay.TotalSeconds);
            return Task.CompletedTask;
        });

        await supervisor.RunAsync([], states.Add, _ => { }, _ => { }, CancellationToken.None);

        Assert.Equal([1d, 2d, 4d, 8d, 16d], delays);
        Assert.Equal(6, runner.Calls);
        Assert.Equal(StreamState.Starting, states[0]);
        Assert.Equal(StreamState.Failed, states[^1]);
        Assert.DoesNotContain(StreamState.Running, states);
    }

    [Fact]
    public async Task ManualStopDuringBackoffPreventsRestart()
    {
        using var stop = new CancellationTokenSource();
        var runner = new Runner((progress, _) =>
        {
            progress(new(Frame: 1));
            return Task.FromResult(1);
        });
        var supervisor = new Supervisor(runner, (_, token) =>
        {
            stop.Cancel();
            return Task.FromCanceled(token);
        });
        var states = new List<StreamState>();

        await supervisor.RunAsync([], states.Add, _ => { }, _ => { }, stop.Token);

        Assert.Equal([StreamState.Starting, StreamState.Running, StreamState.Retrying, StreamState.Idle], states);
        Assert.Equal(1, runner.Calls);
    }
    private sealed class ManualClock : TimeProvider
    {
        private long seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => seconds;
        public void Advance(long interval) => seconds += interval;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StallsAndSparseFrameAdvancesDoNotResetFailures(bool sparse)
    {
        var clock = new ManualClock();
        var runner = new Runner((progress, _) =>
        {
            progress(new(Frame: 1));
            for (var i = 1; i <= 7; i++)
            {
                clock.Advance(10);
                progress(new(Frame: sparse ? i + 1 : 1));
            }
            return Task.FromResult(1);
        });
        var delays = new List<double>();
        var supervisor = new Supervisor(runner, (delay, _) =>
        {
            delays.Add(delay.TotalSeconds);
            // Guard against an accidental infinite reconnect loop.
            Assert.InRange(delays.Count, 1, Supervisor.RetryLimit);
            return Task.CompletedTask;
        }, clock);

        await supervisor.RunAsync([], _ => { }, _ => { }, _ => { }, CancellationToken.None);

        Assert.Equal([1d, 2d, 4d, 8d, 16d], delays);
    }

    [Fact]
    public async Task SustainedAdvancingFramesResetConsecutiveFailures()
    {
        var clock = new ManualClock();
        var attempt = 0;
        var runner = new Runner((progress, _) =>
        {
            if (++attempt == 3)
            {
                for (var frame = 1; frame <= 16; frame++)
                {
                    progress(new(Frame: frame));
                    clock.Advance(4);
                }
                // A later stall does not undo the earned reset.
                clock.Advance(40);
            }
            return Task.FromResult(1);
        });
        var delays = new List<double>();
        var supervisor = new Supervisor(runner, (delay, _) =>
        {
            delays.Add(delay.TotalSeconds);
            return Task.CompletedTask;
        }, clock);

        await supervisor.RunAsync([], _ => { }, _ => { }, _ => { }, CancellationToken.None);

        Assert.Equal([1d, 2d, 1d, 2d, 4d, 8d, 16d], delays);
    }
}

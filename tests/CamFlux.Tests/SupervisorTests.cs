using CamFlux.Core;

namespace CamFlux.Tests;

public sealed class SupervisorTests
{
    private sealed class Runner(Func<Action<string>, CancellationToken, Task<int>> run) : IStreamRunner
    {
        public int Calls { get; private set; }
        public Task<int> RunAsync(string[] arguments, Action<string> progress, Action<string> diagnostic,
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
            progress("1");
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
}

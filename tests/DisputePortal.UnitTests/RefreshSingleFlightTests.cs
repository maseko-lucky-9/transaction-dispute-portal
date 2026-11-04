using DisputePortal.Api.Shared.Security;

namespace DisputePortal.UnitTests;

public sealed class RefreshSingleFlightTests
{
    private static readonly RefreshOutcome Outcome = RefreshOutcome.Gone;

    [Fact]
    public async Task ParallelRefreshesOfOneTokenRunOnce()
    {
        RefreshSingleFlight singleFlight = new();
        TaskCompletionSource<RefreshOutcome> gate = new();
        int calls = 0;

        Task<RefreshOutcome>[] requests = [.. Enumerable.Range(0, 5).Select(_ => singleFlight.RunAsync("rt-1", () =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        }))];
        gate.SetResult(Outcome);
        RefreshOutcome[] results = await Task.WhenAll(requests);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Same(Outcome, result));
    }

    // A finished attempt is never served again: an Unavailable outcome must not stick, and a later request re-reads the ticket.
    [Fact]
    public async Task ACompletedFlightIsNotCached()
    {
        RefreshSingleFlight singleFlight = new();
        int calls = 0;
        Task<RefreshOutcome> Refresh()
        {
            calls++;
            return Task.FromResult(Outcome);
        }

        await singleFlight.RunAsync("rt-1", Refresh);
        await singleFlight.RunAsync("rt-1", Refresh);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DifferentTokensRunIndependently()
    {
        RefreshSingleFlight singleFlight = new();
        TaskCompletionSource<RefreshOutcome> gate = new();
        int calls = 0;
        Task<RefreshOutcome> Refresh()
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        }

        Task<RefreshOutcome> first = singleFlight.RunAsync("rt-1", Refresh);
        Task<RefreshOutcome> second = singleFlight.RunAsync("rt-other", Refresh);
        gate.SetResult(Outcome);
        await Task.WhenAll(first, second);

        Assert.Equal(2, calls);
    }
}

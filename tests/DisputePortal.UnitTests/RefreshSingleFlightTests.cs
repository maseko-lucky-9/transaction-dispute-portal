using System.Security.Claims;
using DisputePortal.Api.Shared.Security;

namespace DisputePortal.UnitTests;

public sealed class RefreshSingleFlightTests
{
    private static readonly RefreshedSession Refreshed = new(new ClaimsIdentity(), "id", "rt-2", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task ParallelRefreshesOfOneTokenRedeemItOnce()
    {
        RefreshSingleFlight singleFlight = new(TimeProvider.System);
        TaskCompletionSource<RefreshedSession?> gate = new();
        int calls = 0;

        Task<RefreshedSession?>[] requests = [.. Enumerable.Range(0, 5).Select(_ => singleFlight.RunAsync("rt-1", () =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        }))];
        gate.SetResult(Refreshed);
        RefreshedSession?[] results = await Task.WhenAll(requests);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.Same(Refreshed, result));
    }

    [Fact]
    public async Task ALateRequestWithTheSameTokenReusesTheResult()
    {
        RefreshSingleFlight singleFlight = new(TimeProvider.System);
        int calls = 0;
        Task<RefreshedSession?> Refresh()
        {
            calls++;
            return Task.FromResult<RefreshedSession?>(Refreshed);
        }

        await singleFlight.RunAsync("rt-1", Refresh);
        await singleFlight.RunAsync("rt-1", Refresh);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DifferentTokensRefreshIndependently()
    {
        RefreshSingleFlight singleFlight = new(TimeProvider.System);
        int calls = 0;
        Task<RefreshedSession?> Refresh()
        {
            calls++;
            return Task.FromResult<RefreshedSession?>(Refreshed);
        }

        await singleFlight.RunAsync("rt-1", Refresh);
        await singleFlight.RunAsync("rt-other", Refresh);

        Assert.Equal(2, calls);
    }
}

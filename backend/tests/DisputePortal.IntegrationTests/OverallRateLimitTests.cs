using System.Globalization;
using System.Net;
using DisputePortal.Api.Shared.Observability;
using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The overall bucket every request shares, <c>RateLimiting:PermitsPerMinute</c> (300 a minute by default), kept per
/// signed-in subject. The tighter 10-a-minute bucket on dispute submit is <see cref="DisputeSecurityTests"/>'s.
/// </summary>
/// <remarks>
/// The limiter is in memory, one per API process: the quota is per replica, so N replicas allow a subject about N times
/// the permits (RateLimits, ponytail: the upgrade is a Redis-backed limiter). The host built below is one replica; this
/// test says nothing about a total across replicas.
/// </remarks>
[Collection("api")]
public sealed class OverallRateLimitTests(ApiFactory api)
{
    private const int Permits = 4;

    [Fact]
    public async Task TheRequestAfterTheQuotaIs429WithRetryAfterCountedOnceAndAnotherSubjectKeepsItsOwnQuota()
    {
        // Signed in on the shared host (a sign-in is anonymous traffic); the cookie works on a host derived from it.
        BrowserSession busy = await BrowserSession.SignInAsync(api, (await TestCustomer.CreateAsync(api)).User);
        BrowserSession quiet = await BrowserSession.SignInAsync(api, (await TestCustomer.CreateAsync(api)).User);
        await using WebApplicationFactory<Program> limited = api.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:PermitsPerMinute", Permits.ToString(CultureInfo.InvariantCulture)));
        using MeasurementProbe<long> probe = MeasurementProbe<long>.On(limited, SecurityMetrics.MeterName, "security.events");
        List<HttpStatusCode> withinQuota = [];

        for (int request = 0; request < Permits; request++)
        {
            withinQuota.Add((await Get(limited, busy)).StatusCode);
        }

        Assert.Empty(probe.Samples);
        HttpResponseMessage rejected = await Get(limited, busy);
        HttpResponseMessage otherSubject = await Get(limited, quiet);

        Assert.All(withinQuota, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(rejected.Headers.RetryAfter?.Delta ?? TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        Assert.Equal(RateLimits.RejectionCode, (await Problem.ReadAsync(rejected)).Code);
        MeasurementProbe<long>.Sample sample = Assert.Single(probe.Samples);
        Assert.Equal(1, sample.Value);
        KeyValuePair<string, object?> tag = Assert.Single(sample.Tags);
        Assert.Equal(("type", "rate_limited"), (tag.Key, tag.Value));
        Assert.Equal(HttpStatusCode.OK, otherSubject.StatusCode);
    }

    private static Task<HttpResponseMessage> Get(WebApplicationFactory<Program> host, BrowserSession session) =>
        AgentApi.SendAsync(host, session.Request(HttpMethod.Get, DisputeRequests.Disputes));
}

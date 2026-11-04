using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DisputePortal.Api.Shared.Errors;
using DisputePortal.Api.Shared.Observability;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Shared.Security;

/// <summary>
/// A fixed window per signed-in subject (per client address when anonymous), after authorization.
/// ponytail: limits are per replica, so N replicas allow about N times the quota; the upgrade is a Redis-backed limiter.
/// </summary>
public static class RateLimits
{
    private const string PermitSetting = "RateLimiting:PermitsPerMinute";
    private const int DefaultPermitsPerMinute = 300;
    private const string DisputeSubmitSetting = "RateLimiting:DisputeSubmitsPerMinute";
    private const int DefaultDisputeSubmitsPerMinute = 10;
    private const string BackchannelLogoutSetting = "RateLimiting:BackchannelLogoutsPerMinute";
    private const int DefaultBackchannelLogoutsPerMinute = 60;

    /// <summary>The tighter bucket on POST /api/v1/disputes, on top of the global one.</summary>
    public const string DisputeSubmitPolicy = "dispute-submit";

    /// <summary>
    /// Keycloak's back-channel logout, per socket peer (the management port ignores X-Forwarded-For). A bound on the
    /// signature checks an unauthenticated caller can make us do; the compose network is trusted, but not assumed to be.
    /// </summary>
    public const string BackchannelLogoutPolicy = "backchannel-logout";

    public const string RejectionCode = "rate_limited";

    public static IServiceCollection AddPerSubjectRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        int permits = configuration.GetValue(PermitSetting, DefaultPermitsPerMinute);
        int disputeSubmits = configuration.GetValue(DisputeSubmitSetting, DefaultDisputeSubmitsPerMinute);
        int backchannelLogouts = configuration.GetValue(BackchannelLogoutSetting, DefaultBackchannelLogoutsPerMinute);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PerMinute(context, permits));
            options.AddPolicy(DisputeSubmitPolicy, context => PerMinute(context, disputeSubmits));
            options.AddPolicy(BackchannelLogoutPolicy, context => PerMinute("peer:" + context.Connection.RemoteIpAddress, backchannelLogouts));
            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().Record(SecurityMetrics.RateLimited);
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                // Written here, like every other refusal, rather than left to the status-code pages.
                await ProblemResponses.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, RejectionCode,
                    "Too many requests for this account; retry after the Retry-After interval.");
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PerMinute(HttpContext context, int permits) => PerMinute(PartitionKey(context), permits);

    private static RateLimitPartition<string> PerMinute(string partitionKey, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimNames.Subject) is { } subject
            ? "sub:" + subject
            : "ip:" + context.Connection.RemoteIpAddress;
}

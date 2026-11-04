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

    /// <summary>The tighter bucket on POST /api/v1/disputes, on top of the global one.</summary>
    public const string DisputeSubmitPolicy = "dispute-submit";

    public const string RejectionCode = "rate_limited";

    public static IServiceCollection AddPerSubjectRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        int permits = configuration.GetValue(PermitSetting, DefaultPermitsPerMinute);
        int disputeSubmits = configuration.GetValue(DisputeSubmitSetting, DefaultDisputeSubmitsPerMinute);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PerMinute(context, permits));
            options.AddPolicy(DisputeSubmitPolicy, context => PerMinute(context, disputeSubmits));
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

    private static RateLimitPartition<string> PerMinute(HttpContext context, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
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

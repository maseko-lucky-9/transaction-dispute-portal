using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
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

    public static IServiceCollection AddPerSubjectRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        int permits = configuration.GetValue(PermitSetting, DefaultPermitsPerMinute);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().Record(SecurityMetrics.RateLimited);
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                // The body comes from the status-code pages: a Problem Details with code too_many_requests.
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimNames.Subject) is { } subject
            ? "sub:" + subject
            : "ip:" + context.Connection.RemoteIpAddress;
}

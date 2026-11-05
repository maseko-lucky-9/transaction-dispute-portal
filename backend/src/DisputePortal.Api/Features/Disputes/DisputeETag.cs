using DisputePortal.Api.Shared.Errors;
using Microsoft.Net.Http.Headers;

namespace DisputePortal.Api.Features.Disputes;

/// <summary>The strong entity tag of a dispute: its row version (xmin) in quotes (ADR-007).</summary>
public static class DisputeETag
{
    /// <summary>
    /// A 412 as an endpoint result, with the current ETag. Endpoints answer it themselves: the exception handler middleware
    /// strips ETag from every response it writes.
    /// </summary>
    public static IResult Stale(HttpContext httpContext, PreconditionFailedException stale)
    {
        if (stale.CurrentETag is not null)
        {
            httpContext.Response.Headers.ETag = stale.CurrentETag;
        }

        return ProblemResponses.Result(stale.ToProblem());
    }

    public static string For(uint version) => "\"" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";

    /// <summary>
    /// True when If-Match names the current version. Weak tags never match (If-Match uses strong comparison), and
    /// <c>*</c> is not accepted: a state change must name the version it was decided on.
    /// </summary>
    public static bool Matches(string ifMatch, uint version)
    {
        string current = For(version);
        return EntityTagHeaderValue.TryParseStrictList([ifMatch], out IList<EntityTagHeaderValue>? tags)
            && tags.Any(tag => !tag.IsWeak && tag.Tag.Equals(current, StringComparison.Ordinal));
    }
}

using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Features.Bff;

/// <param name="ReturnUrl">Where to land after sign-in: a local path only, so the login cannot be used as an open redirect.</param>
public sealed record LoginRequest([FromQuery(Name = "returnUrl")] string? ReturnUrl);

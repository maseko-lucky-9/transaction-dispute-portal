namespace DisputePortal.Api.Features.Bff;

/// <param name="LogoutUrl">Carries the session id, so a cross-site link cannot log the user out.</param>
public sealed record BffUserResponse(string Name, IReadOnlyList<string> Roles, string LogoutUrl);

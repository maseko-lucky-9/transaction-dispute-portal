namespace DisputePortal.Api.Shared.Security;

public enum RefreshStatus
{
    Refreshed,

    /// <summary>The identity provider said no (invalid_grant, invalid ID token): the session is over.</summary>
    Refused,

    /// <summary>The identity provider could not be reached: fail this request closed, but keep the session.</summary>
    Unavailable,
}

public sealed record RefreshResult(RefreshStatus Status, RefreshedSession? Session = null)
{
    public static readonly RefreshResult Refused = new(RefreshStatus.Refused);
    public static readonly RefreshResult Unavailable = new(RefreshStatus.Unavailable);

    public static RefreshResult Refreshed(RefreshedSession session) => new(RefreshStatus.Refreshed, session);
}

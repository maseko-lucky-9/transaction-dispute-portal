namespace DisputePortal.Api.Shared.Security;

/// <summary>JWT claim names, kept as issued (inbound claim mapping is off).</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string PreferredUsername = "preferred_username";
    public const string Roles = "roles";
    public const string SessionId = "sid";
}

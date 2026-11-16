namespace DisputePortal.Api.Shared.Security;

public static class Policies
{
    public const string Customer = "Customer";
    public const string DisputeAgent = "DisputeAgent";

    /// <summary>Either role, for lookups that hold no customer data (a dual-role session is still refused).</summary>
    public const string PortalUser = "PortalUser";
}

namespace DisputePortal.Domain.Disputes;

/// <param name="Id">The identity provider's subject; recorded in the audit trail, never shown to customers.</param>
public sealed record Actor(ActorType Type, string Id)
{
    public static Actor Customer(string subject) => new(ActorType.Customer, subject);

    public static Actor Agent(string subject) => new(ActorType.Agent, subject);
}

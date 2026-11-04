namespace DisputePortal.Domain.Disputes;

/// <summary>Who moved a dispute. Customers see only this, never who the agent was. Stored and sent as its name.</summary>
public enum ActorType
{
    Customer,
    Agent,
}

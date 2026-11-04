namespace DisputePortal.Api.Features.Customers;

/// <summary>A bank customer, linked to exactly one Keycloak user by the token's <c>sub</c>.</summary>
public sealed class Customer
{
    public const int DisplayNameMaxLength = 200;
    public const int SubjectMaxLength = 64;

    public Guid Id { get; init; }

    public string KeycloakSubject { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

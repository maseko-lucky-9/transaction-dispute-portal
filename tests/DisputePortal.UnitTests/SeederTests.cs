using DisputePortal.Api.Shared.Persistence;

namespace DisputePortal.UnitTests;

/// <summary>The subjects are a contract with deploy/keycloak's realm users: changing one breaks sign-in for that user.</summary>
public sealed class SeederTests
{
    [Fact]
    public void TheSeededSubjectsMatchTheRealm()
    {
        Assert.Equal("a1b2c3d4-0000-4000-8000-000000000001", Seeder.ThandiSubject);
        Assert.Equal("a1b2c3d4-0000-4000-8000-000000000002", Seeder.SiphoSubject);
        Assert.Equal("a1b2c3d4-0000-4000-8000-0000000000a1", Seeder.AgentSubject);
    }

    [Fact]
    public void TwoCustomersAreSeededWithTheirNames()
    {
        Assert.Equal(
            [(Seeder.ThandiSubject, "Thandi Nkosi"), (Seeder.SiphoSubject, "Sipho Dlamini")],
            Seeder.Customers.Select(customer => (customer.KeycloakSubject, customer.DisplayName)));
        Assert.Equal(Seeder.Customers.Count, Seeder.Customers.Select(customer => customer.Id).Distinct().Count());
    }

    [Fact]
    public void TheAgentIsNotACustomer() =>
        Assert.DoesNotContain(Seeder.Customers, customer => customer.KeycloakSubject == Seeder.AgentSubject);
}

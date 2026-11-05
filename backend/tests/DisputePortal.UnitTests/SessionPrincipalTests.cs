using System.Security.Claims;
using DisputePortal.Api.Shared.Security;

namespace DisputePortal.UnitTests;

public sealed class SessionPrincipalTests
{
    [Fact]
    public void OnlySubjectNameSessionIdAndAppRolesAreKept()
    {
        ClaimsPrincipal principal = SessionPrincipal.Create(
        [
            new Claim("sub", "user-1"), new Claim("name", "Thandi"), new Claim("sid", "s-1"), new Claim("email", "t@example.test"),
            new Claim("roles", "customer"), new Claim("roles", "offline_access"), new Claim("roles", "customer"),
        ]);

        Assert.Equal(["name", "roles", "sid", "sub"], principal.Claims.Select(claim => claim.Type).Order());
        Assert.Equal(["customer"], SessionPrincipal.RolesOf(principal));
        Assert.True(principal.IsInRole(Roles.Customer));
        Assert.Equal("Thandi", principal.Identity?.Name);
    }

    [Fact]
    public void TheNameFallsBackToThePreferredUsernameThenTheSubject()
    {
        Assert.Equal("thandi", SessionPrincipal.Create([new Claim("sub", "user-1"), new Claim("preferred_username", "thandi")]).Identity?.Name);
        Assert.Equal("user-1", SessionPrincipal.Create([new Claim("sub", "user-1")]).Identity?.Name);
    }

    [Fact]
    public void AMissingSessionIdKeepsThePreviousOne() =>
        Assert.Equal("s-old", SessionPrincipal.Create([new Claim("sub", "user-1")], "s-old").FindFirstValue("sid"));

    [Fact]
    public void ATokenWithoutASubjectIsADefect() =>
        Assert.Throws<InvalidOperationException>(() => SessionPrincipal.Create([new Claim("name", "Nobody")]));
}

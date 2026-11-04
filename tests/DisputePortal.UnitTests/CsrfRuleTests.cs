using DisputePortal.Api.Shared.Security;
using Microsoft.AspNetCore.Http;

namespace DisputePortal.UnitTests;

public sealed class CsrfRuleTests
{
    [Theory]
    [InlineData("GET", "/api/v1/me", true)]
    [InlineData("POST", "/api/v1/disputes", true)]
    [InlineData("GET", "/API/v1/me", true)]
    [InlineData("POST", "/bff/user", true)]
    [InlineData("DELETE", "/bff/session", true)]
    [InlineData("GET", "/bff/user", false)]
    [InlineData("GET", "/bff/login", false)]
    [InlineData("HEAD", "/bff/user", false)]
    [InlineData("GET", "/signin-oidc", false)]
    [InlineData("POST", "/apiary", false)]
    public void TheHeaderIsRequiredOnApiAndOnNonGetBff(string method, string path, bool required)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(required, CsrfMiddleware.RequiresHeader(context.Request));
    }
}

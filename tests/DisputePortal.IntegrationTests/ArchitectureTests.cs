using System.Text.RegularExpressions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DisputePortal.IntegrationTests;

/// <summary>(h) Rules a reviewer would otherwise have to re-check by hand on every PR.</summary>
public sealed partial class ArchitectureTests
{
    private const string AgentFeature = "src/DisputePortal.Api/Features/Agent/";

    [Fact]
    public void OnlyTheAgentFeatureMayBypassQueryFilters()
    {
        string[] offenders = [.. RepositoryFiles.ApiSources()
            .Where(file => file.Text.Contains("IgnoreQueryFilters", StringComparison.Ordinal))
            .Where(file => !file.Path.StartsWith(AgentFeature, StringComparison.Ordinal))
            .Select(file => file.Path)];

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoRawSqlCallIsBuiltFromAnInterpolatedOrConcatenatedString()
    {
        string[] offenders = [.. RepositoryFiles.ApiSources()
            .Where(file => UnsafeRawSql().IsMatch(file.Text))
            .Select(file => file.Path)];

        Assert.Empty(offenders);
    }

    // Guards the guard: the pattern must catch what it claims to catch.
    [Theory]
    [InlineData("db.Customers.FromSqlRaw($\"SELECT * FROM customers WHERE id = {id}\")")]
    [InlineData("db.Database.ExecuteSqlRawAsync($@\"DELETE FROM x WHERE id = {id}\")")]
    [InlineData("db.Database.SqlQueryRaw<int>(\"SELECT \" + column)")]
    public void TheRawSqlRuleCatchesUnsafeCalls(string code) => Assert.Matches(UnsafeRawSql(), code);

    [Fact]
    public void TheRawSqlRuleAllowsParameterisedCalls() =>
        Assert.DoesNotMatch(UnsafeRawSql(), "db.Customers.FromSql($\"SELECT * FROM customers WHERE id = {id}\")");

    [Fact]
    public void TheDomainReferencesNeitherEfCoreNorAspNetCore()
    {
        string[] references = [.. typeof(Money).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? "")];

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    // Fail-closed ownership is only as good as its coverage: any entity carrying a CustomerId must carry the Owner filter.
    [Fact]
    public void EveryEntityWithACustomerIdHasTheOwnerFilter()
    {
        using DisputePortalDbContext db = new DesignTimeDbContextFactory().CreateDbContext([]);
        IEntityType[] owned = [.. db.Model.GetEntityTypes().Where(entity => entity.FindProperty("CustomerId") is not null)];

        Assert.Equal(["Account", "Transaction"], owned.Select(entity => entity.ClrType.Name).Order(StringComparer.Ordinal));
        Assert.All(owned, entity => Assert.Contains(entity.GetDeclaredQueryFilters(), filter => filter.Key == DisputePortalDbContext.OwnerFilter));
    }

    [GeneratedRegex(@"\b(?:FromSqlRaw|ExecuteSqlRaw(?:Async)?|SqlQueryRaw)\s*(?:<[^>]*>)?\(\s*(?:\$|""[^""]*""\s*\+)")]
    private static partial Regex UnsafeRawSql();
}

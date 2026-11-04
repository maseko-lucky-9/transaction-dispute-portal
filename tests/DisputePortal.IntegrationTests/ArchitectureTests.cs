using System.Text.RegularExpressions;
using DisputePortal.Api.Shared.Persistence;
using DisputePortal.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DisputePortal.IntegrationTests;

/// <summary>(h) Rules a reviewer would otherwise have to re-check by hand on every PR.</summary>
public sealed partial class ArchitectureTests
{
    private const string AgentFeature = "src/DisputePortal.Api/Features/Agent/";
    private const string PersistenceFolder = "src/DisputePortal.Api/Shared/Persistence/";
    private const string HealthFolder = "src/DisputePortal.Api/Shared/Health/";

    // Raw SQL is not entity-scoped, so the Owner filter never applies to it; the agent feature is the deliberate exception.
    private static readonly string[] RawSqlAllowedIn = [PersistenceFolder, AgentFeature];

    // A connection or command of its own skips EF, and the filter with it; the migration runner and the health check need one.
    private static readonly string[] DirectNpgsqlAllowedIn = [PersistenceFolder, HealthFolder, AgentFeature];

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
    public void OnlyPersistenceAndTheAgentFeatureRunRawSqlThroughEf() =>
        Assert.Empty(Offenders(RepositoryFiles.ApiSources(), RawSqlEntryPoint(), RawSqlAllowedIn));

    [Fact]
    public void OnlyPersistenceHealthAndTheAgentFeatureOpenTheirOwnNpgsqlConnections() =>
        Assert.Empty(Offenders(RepositoryFiles.ApiSources(), DirectNpgsqlAccess(), DirectNpgsqlAllowedIn));

    [Theory]
    [InlineData("db.Database.SqlQuery<int>($\"SELECT count(*) FROM transactions\")")]
    [InlineData("db.Database.SqlQueryRaw<int>(\"SELECT 1\")")]
    [InlineData("db.Transactions.FromSql($\"SELECT * FROM transactions\")")]
    [InlineData("db.Transactions.FromSqlInterpolated($\"SELECT * FROM transactions\")")]
    [InlineData("await db.Database.ExecuteSqlAsync($\"DELETE FROM transactions\")")]
    [InlineData("db.Database.ExecuteSqlRaw(\"DELETE FROM transactions\")")]
    [InlineData("DbConnection connection = db.Database.GetDbConnection();")]
    public void TheRawSqlEntryPointRuleCatchesEveryEfEntryPoint(string code) =>
        Assert.Single(Offenders([("src/DisputePortal.Api/Features/Transactions/Probe.cs", code)], RawSqlEntryPoint(), RawSqlAllowedIn));

    [Theory]
    [InlineData("public sealed class Probe(NpgsqlDataSource dataSource)")]
    [InlineData("NpgsqlDataSource source = new NpgsqlDataSourceBuilder(text).Build();")]
    [InlineData("await using NpgsqlConnection connection = new(text);")]
    [InlineData("await using NpgsqlCommand command = dataSource.CreateCommand(sql);")]
    [InlineData("await using NpgsqlBatch batch = dataSource.CreateBatch();")]
    public void TheDirectNpgsqlRuleCatchesConnectionsCommandsAndDataSources(string code) =>
        Assert.Single(Offenders([("src/DisputePortal.Api/Features/Transactions/Probe.cs", code)], DirectNpgsqlAccess(), DirectNpgsqlAllowedIn));

    // The allow-lists are exact: health may ping, but may not run raw SQL through EF; nothing else is exempt.
    [Theory]
    [InlineData("src/DisputePortal.Api/Shared/Health/Probe.cs", false, true)]
    [InlineData("src/DisputePortal.Api/Shared/Persistence/Probe.cs", true, true)]
    [InlineData("src/DisputePortal.Api/Features/Agent/Probe.cs", true, true)]
    [InlineData("src/DisputePortal.Api/Shared/Security/Probe.cs", false, false)]
    [InlineData("src/DisputePortal.Api/Features/Transactions/Probe.cs", false, false)]
    public void TheDatabaseAccessAllowListsAreExact(string path, bool rawSqlAllowed, bool npgsqlAllowed)
    {
        (string, string)[] probe = [(path, "db.Database.SqlQueryRaw<int>(sql); await using NpgsqlConnection connection = new(text);")];

        Assert.Equal(rawSqlAllowed, Offenders(probe, RawSqlEntryPoint(), RawSqlAllowedIn).Length == 0);
        Assert.Equal(npgsqlAllowed, Offenders(probe, DirectNpgsqlAccess(), DirectNpgsqlAllowedIn).Length == 0);
    }

    [Theory]
    [InlineData("migrationBuilder.Sql(\"GRANT SELECT ON accounts TO dispute_app;\");")]
    [InlineData("NpgsqlConnectionStringBuilder builder = new(text);")]
    [InlineData("catch (NpgsqlException exception)")]
    [InlineData("int SqlQueryCount = 0;")]
    public void TheDatabaseAccessRulesIgnoreLookalikes(string code)
    {
        (string, string)[] probe = [("src/DisputePortal.Api/Features/Transactions/Probe.cs", code)];

        Assert.Empty(Offenders(probe, RawSqlEntryPoint(), RawSqlAllowedIn));
        Assert.Empty(Offenders(probe, DirectNpgsqlAccess(), DirectNpgsqlAllowedIn));
    }

    // Agents see every dispute, so their 404 is a missing id and not a probe: only the customer slices count bola_404.
    [Fact]
    public void TheAgentFeatureNeverCountsBola404()
    {
        string[] offenders = [.. RepositoryFiles.ApiSources()
            .Where(file => file.Path.StartsWith(AgentFeature, StringComparison.Ordinal))
            .Where(file => CountsBola404().IsMatch(file.Text))
            .Select(file => file.Path)];

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("securityMetrics.Record(SecurityMetrics.Bola404);")]
    [InlineData("throw DisputeNotFound.ToCustomer(securityMetrics);")]
    [InlineData("throw DisputeNotFound . ToCustomer( securityMetrics );")]
    public void TheBola404RuleCatchesTheCountingCalls(string code) =>
        Assert.Matches(CountsBola404(), code);

    [Fact]
    public void TheBola404RuleAllowsTheAgentsUncountedAnswer() =>
        Assert.DoesNotMatch(CountsBola404(), "throw DisputeNotFound.ToAgent();");

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

        Assert.Equal(["Account", "Dispute", "Transaction"], owned.Select(entity => entity.ClrType.Name).Order(StringComparer.Ordinal));
        Assert.All(owned, entity => Assert.True(HasOwnerFilter(entity), entity.ClrType.Name));
    }

    // Rows reached through an owned row (a dispute's history, say) carry no CustomerId of their own, but are just as private.
    [Fact]
    public void EveryEntityThatReferencesAnOwnedEntityHasTheOwnerFilter()
    {
        using DisputePortalDbContext db = new DesignTimeDbContextFactory().CreateDbContext([]);
        IEntityType[] dependents = [.. db.Model.GetEntityTypes()
            .Where(entity => entity.GetForeignKeys().Any(foreignKey => HasOwnerFilter(foreignKey.PrincipalEntityType)))];

        Assert.Equal(["Dispute", "DisputeStatusChange", "Transaction"], dependents.Select(entity => entity.ClrType.Name).Order(StringComparer.Ordinal));
        Assert.All(dependents, entity => Assert.True(HasOwnerFilter(entity), entity.ClrType.Name));
    }

    private static bool HasOwnerFilter(IReadOnlyEntityType entity) =>
        entity.GetDeclaredQueryFilters().Any(filter => filter.Key == DisputePortalDbContext.OwnerFilter);

    private static string[] Offenders(IEnumerable<(string Path, string Text)> files, Regex rule, string[] allowedIn) =>
        [.. files
            .Where(file => rule.IsMatch(file.Text))
            .Where(file => !allowedIn.Any(folder => file.Path.StartsWith(folder, StringComparison.Ordinal)))
            .Select(file => file.Path)];

    [GeneratedRegex(@"\b(?:SqlQuery(?:Raw)?|FromSql\w*|ExecuteSql\w*|GetDbConnection)\s*(?:<[^>]*>)?\(")]
    private static partial Regex RawSqlEntryPoint();

    [GeneratedRegex(@"\bNpgsql(?:DataSource(?:Builder)?|Connection|Command|Batch)\b")]
    private static partial Regex DirectNpgsqlAccess();

    [GeneratedRegex(@"\bBola404\b|\bDisputeNotFound\s*\.\s*ToCustomer\b")]
    private static partial Regex CountsBola404();

    [GeneratedRegex(@"\b(?:FromSqlRaw|ExecuteSqlRaw(?:Async)?|SqlQueryRaw)\s*(?:<[^>]*>)?\(\s*(?:\$|""[^""]*""\s*\+)")]
    private static partial Regex UnsafeRawSql();
}

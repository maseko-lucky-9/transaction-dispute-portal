using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The AgentAssignment migration (ADR-019) takes ACCESS EXCLUSIVE on disputes. Behind a transaction holding the table it
/// gives up after its lock_timeout (55P03) and rolls back whole, instead of queueing every disputes query behind it; the
/// next run succeeds. The timeout is SET LOCAL inside the migration's transaction, so it never stays on the session.
/// </summary>
[Collection("api")]
public sealed class AgentAssignmentMigrationTests(ApiFactory api)
{
    private const string Previous = "20261104140437_AgentQueue";
    private const string AgentAssignment = "20261116111226_AgentAssignment";
    private const string LockNotAvailable = "55P03";

    // The migrate run's retrying execution strategy gives up after about 24 s here (observed; consistent with 5 s
    // attempts retried with back-off), as the migrate Job would. Well past that: without its lock_timeout the migration
    // would wait on the blocker for as long as the blocker lives.
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task TheScriptSetsTheLockTimeoutInsideTheMigrationsTransactionBeforeTheAlter()
    {
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(api.AdminConnectionString);
        await using DisputePortalDbContext db = ContextOn(source);

        string script = db.GetService<IMigrator>().GenerateScript(Previous, AgentAssignment);

        int begin = script.IndexOf("START TRANSACTION;", StringComparison.Ordinal);
        int setLocal = script.IndexOf("SET LOCAL lock_timeout = '5s';", StringComparison.Ordinal);
        int alter = script.IndexOf("ALTER TABLE disputes ADD assignee_id", StringComparison.Ordinal);
        int commit = script.IndexOf("COMMIT;", alter, StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < setLocal && setLocal < alter && alter < commit, script);
    }

    [Fact]
    public async Task BehindATransactionHoldingTheTableItGivesUpRollsBackAndTheRetrySucceeds()
    {
        (string database, _, _) = await api.CreateEmptyDatabaseAsync();
        string admin = api.AdminConnectionStringFor(database);
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(admin);
        await using DisputePortalDbContext db = ContextOn(source);
        // As MigrationRunner does: the app's 10 s command timeout would cut a blocked ALTER off on its own.
        db.Database.SetCommandTimeout(MigrationRunner.CommandTimeout);
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);

        await using (NpgsqlConnection blocker = await source.OpenConnectionAsync())
        await using (NpgsqlTransaction holding = await blocker.BeginTransactionAsync())
        {
            // ACCESS SHARE, held until the transaction ends: enough to block ACCESS EXCLUSIVE.
            await using NpgsqlCommand read = new("SELECT count(*) FROM disputes", blocker, holding);
            await read.ExecuteScalarAsync();

            Task migrating = migrator.MigrateAsync(AgentAssignment);
            if (await Task.WhenAny(migrating, Task.Delay(GiveUpAfter)) != migrating)
            {
                // Free the lock so the stuck migration finishes before the test tears its context down.
                await holding.RollbackAsync();
                await migrating;
                Assert.Fail($"The migration was still waiting for its lock after {GiveUpAfter}: it has no lock_timeout.");
            }

            Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => migrating);

            Assert.Equal(LockNotAvailable, (refused as PostgresException ?? refused.InnerException as PostgresException)?.SqlState);
            Assert.Equal((0L, 0L), await SchemaStateAsync(admin));
            await holding.RollbackAsync();
        }

        // The retry, on one session held open, so a setting the migration left behind would still be visible on it.
        await db.Database.OpenConnectionAsync();
        await migrator.MigrateAsync(AgentAssignment);
        List<string> lockTimeout = await db.Database.SqlQueryRaw<string>("""SELECT current_setting('lock_timeout') AS "Value" """).ToListAsync();
        await db.Database.CloseConnectionAsync();

        Assert.Equal((1L, 1L), await SchemaStateAsync(admin));
        Assert.Equal(["0"], lockTimeout);
    }

    // Design-time configuration (no seeding): the migrations alone, against the database the data source points at.
    internal static DisputePortalDbContext ContextOn(NpgsqlDataSource source)
    {
        DbContextOptionsBuilder<DisputePortalDbContext> options = new();
        PersistenceRegistration.Configure(options, source);
        return new DisputePortalDbContext(options.Options, new CurrentCustomer());
    }

    // (assignee_id column present, AgentAssignment recorded as applied): both or neither.
    private static async Task<(long Column, long Applied)> SchemaStateAsync(string admin) => (
        await AdminSql.CountAsync(admin, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'disputes' AND column_name = 'assignee_id'"),
        await AdminSql.CountAsync(admin, """SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id = @id""", ("id", AgentAssignment)));
}

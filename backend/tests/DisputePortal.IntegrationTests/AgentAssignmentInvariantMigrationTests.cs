using DisputePortal.Api.Features.Disputes;
using DisputePortal.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The AgentAssignmentInvariant migration (ADR-019, the contract step), on a fresh database brought to the previous
/// migration and seeded with what old pods could have left behind: it assigns UnderReview rows from their history,
/// clears stray assignees, refuses to invent an assignee, and ends with ck_disputes_assignee present and validated.
/// </summary>
[Collection("api")]
public sealed class AgentAssignmentInvariantMigrationTests(ApiFactory api)
{
    private const string Previous = "20261116111226_AgentAssignment";
    private const string Invariant = "20261116140513_AgentAssignmentInvariant";
    private const string RaiseException = "P0001";
    private const string RunbookStep = "\"The AgentAssignmentInvariant migration aborted\"";

    private static readonly Guid CustomerId = Guid.NewGuid();

    private const string SeedParentsSql = """
        INSERT INTO customers (id, keycloak_subject, display_name) VALUES (@customer, 'migration-customer', 'Migration Customer');
        INSERT INTO accounts (id, customer_id, masked_number, currency) VALUES (@account, @customer, '**** 0001', 'ZAR');
        """;

    // A dispute on its own transaction (so the one-active-per-transaction index never interferes), with its first
    // history entry. The status and assignee are written as given: this is the state old pods could leave.
    private const string SeedDisputeSql = """
        INSERT INTO transactions (id, account_id, customer_id, reference, merchant_name, category, amount, currency, direction, status, transaction_date, posted_at)
        VALUES (@transaction, @account, @customer, 'M' || left(replace(@transaction::text, '-', ''), 20), 'Probe', 'Test', 10, 'ZAR', 'Debit', 'Posted', now() - interval '2 days', now() - interval '2 days');
        INSERT INTO disputes (id, transaction_id, customer_id, reason_code, description, disputed_amount, currency, status, created_at, updated_at, resolved_at, assignee_id)
        VALUES (@id, @transaction, @customer, 'other', 'Probe dispute.', 10, 'ZAR', @status, now() - interval '1 day', now() - interval '1 day',
                CASE WHEN @status IN ('Accepted', 'Rejected', 'Withdrawn') THEN now() END, @assignee);
        INSERT INTO dispute_status_changes (dispute_id, from_status, to_status, actor_type, actor_id, occurred_at)
        VALUES (@id, NULL, 'Submitted', 'Customer', 'migration-customer', now() - interval '1 day');
        """;

    private const string SeedMoveSql = """
        INSERT INTO dispute_status_changes (dispute_id, from_status, to_status, actor_type, actor_id, occurred_at)
        VALUES (@id, @from, @to, 'Agent', @actor, @at);
        """;

    [Fact]
    public async Task TheScriptLocksBackfillsClearsChecksAndAddsTheConstraintInOneTransaction()
    {
        await using NpgsqlDataSource source = NpgsqlDataSource.Create(api.AdminConnectionString);
        await using DisputePortalDbContext db = AgentAssignmentMigrationTests.ContextOn(source);

        string script = db.GetService<IMigrator>().GenerateScript(Previous, Invariant);

        int[] order =
        [
            script.IndexOf("START TRANSACTION;", StringComparison.Ordinal),
            script.IndexOf("SET LOCAL lock_timeout = '5s';", StringComparison.Ordinal),
            script.IndexOf("LOCK TABLE disputes IN ACCESS EXCLUSIVE MODE;", StringComparison.Ordinal),
            script.IndexOf("WHERE d.status = 'UnderReview' AND d.assignee_id IS NULL;", StringComparison.Ordinal),
            script.IndexOf("WHERE status <> 'UnderReview' AND assignee_id IS NOT NULL;", StringComparison.Ordinal),
            script.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal),
            script.IndexOf("ADD CONSTRAINT ck_disputes_assignee", StringComparison.Ordinal),
            script.IndexOf("COMMIT;", StringComparison.Ordinal),
        ];
        Assert.True(order[0] >= 0 && order.Zip(order.Skip(1)).All(pair => pair.First < pair.Second), script);
        Assert.Equal(1, script.Split("START TRANSACTION;").Length - 1);
    }

    [Fact]
    public async Task ItAssignsFromTheHistoryClearsStraysLeavesCleanRowsAndEndsValidated()
    {
        (string admin, IMigrator migrator, DisputePortalDbContext db, NpgsqlDataSource source) = await DatabaseAtPreviousAsync();
        await using (source)
        await using (db)
        {
            Guid account = await SeedParentsAsync(admin);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            // Claimed by agent-a, released, then claimed by agent-c and agent-b at the same instant (agent-b recorded
            // last): the latest move to UnderReview wins, ties broken by the later history id.
            Guid unassigned = await SeedDisputeAsync(admin, account, "UnderReview", assignee: null);
            await SeedMoveAsync(admin, unassigned, "Submitted", "UnderReview", "agent-a", now.AddHours(-3));
            await SeedMoveAsync(admin, unassigned, "UnderReview", "Submitted", "agent-a", now.AddHours(-2));
            await SeedMoveAsync(admin, unassigned, "Submitted", "UnderReview", "agent-c", now.AddHours(-1));
            await SeedMoveAsync(admin, unassigned, "Submitted", "UnderReview", "agent-b", now.AddHours(-1));
            Guid strayOnSubmitted = await SeedDisputeAsync(admin, account, "Submitted", assignee: "stray-agent-1");
            Guid strayOnWithdrawn = await SeedDisputeAsync(admin, account, "Withdrawn", assignee: "stray-agent-2");
            // Its history names someone else: a backfill that ignored the existing assignee would overwrite it.
            Guid clean = await SeedDisputeAsync(admin, account, "UnderReview", assignee: "agent-clean");
            await SeedMoveAsync(admin, clean, "Submitted", "UnderReview", "agent-other", now.AddHours(-1));
            uint cleanVersion = await VersionAsync(admin, clean);

            await migrator.MigrateAsync(Invariant);

            Assert.Equal("agent-b", await AssigneeAsync(admin, unassigned));
            Assert.Null(await AssigneeAsync(admin, strayOnSubmitted));
            Assert.Null(await AssigneeAsync(admin, strayOnWithdrawn));
            Assert.Equal(("agent-clean", cleanVersion), (await AssigneeAsync(admin, clean), await VersionAsync(admin, clean)));
            Assert.Equal([true], await ConstraintValidatedAsync(admin));
            Assert.Equal(1L, await AppliedAsync(admin, Invariant));
        }
    }

    [Fact]
    public async Task AnUnderReviewRowWithNoHistoryAbortsItWithTheCountAndChangesNothing()
    {
        (string admin, IMigrator migrator, DisputePortalDbContext db, NpgsqlDataSource source) = await DatabaseAtPreviousAsync();
        await using (source)
        await using (db)
        {
            Guid account = await SeedParentsAsync(admin);
            Guid orphan = await SeedDisputeAsync(admin, account, "UnderReview", assignee: null);
            Guid backfillable = await SeedDisputeAsync(admin, account, "UnderReview", assignee: null);
            await SeedMoveAsync(admin, backfillable, "Submitted", "UnderReview", "agent-a", DateTimeOffset.UtcNow.AddHours(-1));
            Guid stray = await SeedDisputeAsync(admin, account, "Submitted", assignee: "stray-agent");

            Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(Invariant));

            // The PostgresException itself, not wrapped in EF's retry-limit exception: P0001 is not retried.
            PostgresException postgres = Assert.IsType<PostgresException>(refused);
            Assert.Equal(RaiseException, postgres.SqlState);
            Assert.Contains("1 dispute(s) are UnderReview with no assignee", postgres.MessageText, StringComparison.Ordinal);
            Assert.Contains(RunbookStep, postgres.MessageText, StringComparison.Ordinal);
            Assert.DoesNotContain(orphan.ToString(), postgres.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(CustomerId.ToString(), postgres.MessageText, StringComparison.OrdinalIgnoreCase);
            // Rolled back whole: the backfill and the clean-up before the abort are gone with it.
            Assert.Equal(((string?)null, (string?)null, "stray-agent"), (await AssigneeAsync(admin, orphan), await AssigneeAsync(admin, backfillable), await AssigneeAsync(admin, stray)));
            Assert.Empty(await ConstraintValidatedAsync(admin));
            Assert.Equal((0L, 1L), (await AppliedAsync(admin, Invariant), await AppliedAsync(admin, Previous)));
        }
    }

    [Fact]
    public async Task DownDropsTheConstraintAndNothingElse()
    {
        (string admin, IMigrator migrator, DisputePortalDbContext db, NpgsqlDataSource source) = await DatabaseAtPreviousAsync();
        await using (source)
        await using (db)
        {
            Guid account = await SeedParentsAsync(admin);
            Guid claimed = await SeedDisputeAsync(admin, account, "UnderReview", assignee: "agent-a");
            await migrator.MigrateAsync(Invariant);

            await migrator.MigrateAsync(Previous);

            Assert.Empty(await ConstraintValidatedAsync(admin));
            Assert.Equal((0L, 1L), (await AppliedAsync(admin, Invariant), await AppliedAsync(admin, Previous)));
            Assert.Equal("agent-a", await AssigneeAsync(admin, claimed));
            Assert.Equal(1L, await AdminSql.CountAsync(admin, "SELECT count(*) FROM pg_indexes WHERE tablename = 'disputes' AND indexname = @name", ("name", DisputeConfiguration.AssigneeQueueIndexName)));
            Assert.Equal(1L, await AdminSql.CountAsync(admin, "SELECT count(*) WHERE has_column_privilege('dispute_app', 'disputes', 'assignee_id', 'UPDATE')"));
            // The other dispute CHECKs stay.
            Assert.Equal(4L, await AdminSql.CountAsync(admin, "SELECT count(*) FROM pg_constraint WHERE conrelid = 'disputes'::regclass AND contype = 'c'"));
        }
    }

    private async Task<(string Admin, IMigrator Migrator, DisputePortalDbContext Db, NpgsqlDataSource Source)> DatabaseAtPreviousAsync()
    {
        (string database, _, _) = await api.CreateEmptyDatabaseAsync();
        string admin = api.AdminConnectionStringFor(database);
        NpgsqlDataSource source = NpgsqlDataSource.Create(admin);
        DisputePortalDbContext db = AgentAssignmentMigrationTests.ContextOn(source);
        db.Database.SetCommandTimeout(MigrationRunner.CommandTimeout);
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        return (admin, migrator, db, source);
    }

    private static async Task<Guid> SeedParentsAsync(string admin)
    {
        Guid account = Guid.NewGuid();
        await ExecuteAsync(admin, SeedParentsSql, ("customer", CustomerId), ("account", account));
        return account;
    }

    private static async Task<Guid> SeedDisputeAsync(string admin, Guid account, string status, string? assignee)
    {
        Guid id = Guid.NewGuid();
        await ExecuteAsync(admin, SeedDisputeSql,
            ("id", id), ("transaction", Guid.NewGuid()), ("account", account), ("customer", CustomerId), ("status", status), ("assignee", (object?)assignee ?? DBNull.Value));
        return id;
    }

    private static Task SeedMoveAsync(string admin, Guid id, string from, string to, string actor, DateTimeOffset at) =>
        ExecuteAsync(admin, SeedMoveSql, ("id", id), ("from", from), ("to", to), ("actor", actor), ("at", at));

    private static async Task ExecuteAsync(string admin, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(admin);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> AssigneeAsync(string admin, Guid id) =>
        (await AdminSql.ListAsync(admin, "SELECT assignee_id FROM disputes WHERE id = @id", reader => reader.IsDBNull(0) ? null : reader.GetString(0), ("id", id))).Single();

    private static async Task<uint> VersionAsync(string admin, Guid id) =>
        (await AdminSql.ListAsync(admin, "SELECT xmin FROM disputes WHERE id = @id", reader => reader.GetFieldValue<uint>(0), ("id", id))).Single();

    // One entry per ck_disputes_assignee on disputes: whether it is validated. Empty when the constraint is absent.
    private static Task<List<bool>> ConstraintValidatedAsync(string admin) =>
        AdminSql.ListAsync(admin, "SELECT convalidated FROM pg_constraint WHERE conrelid = 'disputes'::regclass AND conname = @name",
            reader => reader.GetBoolean(0), ("name", DisputeConfiguration.AssigneeCheckName));

    private static Task<long> AppliedAsync(string admin, string migration) =>
        AdminSql.CountAsync(admin, """SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id = @id""", ("id", migration));
}

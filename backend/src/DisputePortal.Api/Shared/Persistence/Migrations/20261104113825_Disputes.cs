using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DisputePortal.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Disputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_transactions_id_customer_id",
                table: "transactions",
                columns: new[] { "id", "customer_id" });

            migrationBuilder.CreateTable(
                name: "dispute_reasons",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_reasons", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    disputed_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                    table.CheckConstraint("ck_disputes_amount_positive", "disputed_amount > 0");
                    table.CheckConstraint("ck_disputes_resolution_note", "(status IN ('Accepted', 'Rejected')) = (resolution_note IS NOT NULL)");
                    table.CheckConstraint("ck_disputes_resolved_at", "(status IN ('Accepted', 'Rejected', 'Withdrawn')) = (resolved_at IS NOT NULL)");
                    table.CheckConstraint("ck_disputes_status", "status IN ('Submitted', 'UnderReview', 'Accepted', 'Rejected', 'Withdrawn')");
                    table.ForeignKey(
                        name: "fk_disputes_dispute_reasons_reason_code",
                        column: x => x.reason_code,
                        principalTable: "dispute_reasons",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_transactions_transaction_id_customer_id",
                        columns: x => new { x.transaction_id, x.customer_id },
                        principalTable: "transactions",
                        principalColumns: new[] { "id", "customer_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute_status_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_status_changes", x => x.id);
                    table.CheckConstraint("ck_dispute_status_changes_actor_type", "actor_type IN ('Customer', 'Agent')");
                    table.CheckConstraint("ck_dispute_status_changes_from_status", "from_status IS NULL OR from_status IN ('Submitted', 'UnderReview', 'Accepted', 'Rejected', 'Withdrawn')");
                    table.CheckConstraint("ck_dispute_status_changes_to_status", "to_status IN ('Submitted', 'UnderReview', 'Accepted', 'Rejected', 'Withdrawn')");
                    table.ForeignKey(
                        name: "fk_dispute_status_changes_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "dispute_reasons",
                columns: new[] { "code", "description", "label", "sort_order" },
                values: new object[,]
                {
                    { "card_lost_or_stolen", "The transaction happened after your card was lost or stolen.", "Card lost or stolen", 6 },
                    { "duplicate_charge", "The same purchase was charged to your account more than once.", "Charged more than once", 2 },
                    { "goods_not_received", "You paid but did not receive what you ordered.", "Goods or services not received", 4 },
                    { "incorrect_amount", "You were charged a different amount from the one you agreed to pay.", "Wrong amount charged", 3 },
                    { "other", "Another problem; describe it in your own words.", "Something else", 7 },
                    { "subscription_cancelled", "You were charged for a subscription you had already cancelled.", "Subscription already cancelled", 5 },
                    { "unrecognised_transaction", "You did not make or authorise this transaction.", "I don't recognise this transaction", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_status_changes_dispute_id_occurred_at",
                table: "dispute_status_changes",
                columns: new[] { "dispute_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_customer_id_created_at_id",
                table: "disputes",
                columns: new[] { "customer_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_reason_code",
                table: "disputes",
                column: "reason_code");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_created_at_id",
                table: "disputes",
                columns: new[] { "status", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_transaction_id_customer_id_created_at_id",
                table: "disputes",
                columns: new[] { "transaction_id", "customer_id", "created_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_disputes_one_active_per_transaction",
                table: "disputes",
                column: "transaction_id",
                unique: true,
                filter: "status IN ('Submitted', 'UnderReview')");

            // Least privilege for the app role (dispute_app). The reason catalogue is read-only. Disputes may be read,
            // created and moved, but only the lifecycle columns can change and nothing can be deleted. The history is
            // append-only, and occurred_at is left out of the INSERT grant so only the column default can stamp it.
            migrationBuilder.Sql("GRANT SELECT ON dispute_reasons TO dispute_app;");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON disputes TO dispute_app;");
            migrationBuilder.Sql("GRANT UPDATE (status, updated_at, resolved_at, resolution_note) ON disputes TO dispute_app;");
            migrationBuilder.Sql("GRANT SELECT ON dispute_status_changes TO dispute_app;");
            migrationBuilder.Sql("GRANT INSERT (dispute_id, from_status, to_status, actor_type, actor_id, note) ON dispute_status_changes TO dispute_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_status_changes");

            migrationBuilder.DropTable(
                name: "disputes");

            migrationBuilder.DropTable(
                name: "dispute_reasons");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_transactions_id_customer_id",
                table: "transactions");
        }
    }
}

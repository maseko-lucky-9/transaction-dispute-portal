using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_disputes_open_queue",
                table: "disputes",
                columns: new[] { "created_at", "id" },
                filter: "status IN ('Submitted', 'UnderReview')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_disputes_open_queue",
                table: "disputes");
        }
    }
}

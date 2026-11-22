using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddFairClaimIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_hookwright_deliveries_state_next_attempt_at",
            table: "hookwright_deliveries");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_state_endpoint_id_next_attempt_at",
            table: "hookwright_deliveries",
            columns: ["state", "endpoint_id", "next_attempt_at"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_hookwright_deliveries_state_endpoint_id_next_attempt_at",
            table: "hookwright_deliveries");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_state_next_attempt_at",
            table: "hookwright_deliveries",
            columns: ["state", "next_attempt_at"]);
    }
}
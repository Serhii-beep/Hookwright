using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddRetentionIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_hookwright_delivery_attempts_attempted_at",
            table: "hookwright_delivery_attempts",
            column: "attempted_at");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_completed_at",
            table: "hookwright_deliveries",
            column: "completed_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_hookwright_delivery_attempts_attempted_at",
            table: "hookwright_delivery_attempts");

        migrationBuilder.DropIndex(
            name: "ix_hookwright_deliveries_completed_at",
            table: "hookwright_deliveries");
    }
}
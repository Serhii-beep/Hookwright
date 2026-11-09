using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.Sqlite.Migrations;

/// <inheritdoc />
public partial class AddEndpointFailingSince : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "failing_since",
            table: "hookwright_endpoints",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "failing_since",
            table: "hookwright_endpoints");
    }
}
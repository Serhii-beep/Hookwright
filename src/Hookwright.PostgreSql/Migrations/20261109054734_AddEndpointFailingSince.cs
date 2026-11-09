using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddEndpointFailingSince : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "failing_since",
            table: "hookwright_endpoints",
            type: "timestamp with time zone",
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
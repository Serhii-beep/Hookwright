using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.SqlServer.Migrations;

/// <inheritdoc />
public partial class AddEndpointFailingSince : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "failing_since",
            table: "hookwright_endpoints",
            type: "datetimeoffset",
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
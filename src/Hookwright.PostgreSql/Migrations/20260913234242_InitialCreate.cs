using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookwright.PostgreSql.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "hookwright_event_types",
            columns: table => new
            {
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                schema_json = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                is_archived = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_event_types", x => x.name);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_subscribers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_subscribers", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_endpoints",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                partition_mode = table.Column<short>(type: "smallint", nullable: false),
                rate_limit_per_second = table.Column<int>(type: "integer", nullable: true),
                health = table.Column<short>(type: "smallint", nullable: false),
                consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                disabled_reason = table.Column<string>(type: "text", nullable: true),
                disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                event_type_filter = table.Column<List<string>>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_endpoints", x => x.id);
                table.ForeignKey(
                    name: "FK_hookwright_endpoints_hookwright_subscribers_subscriber_id",
                    column: x => x.subscriber_id,
                    principalTable: "hookwright_subscribers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_events",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                subscriber_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                partition_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                headers = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_events", x => x.id);
                table.ForeignKey(
                    name: "FK_hookwright_events_hookwright_subscribers_subscriber_id",
                    column: x => x.subscriber_id,
                    principalTable: "hookwright_subscribers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_endpoint_secrets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                protected_key = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                algorithm = table.Column<short>(type: "smallint", nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_endpoint_secrets", x => x.id);
                table.ForeignKey(
                    name: "FK_hookwright_endpoint_secrets_hookwright_endpoints_endpoint_id",
                    column: x => x.endpoint_id,
                    principalTable: "hookwright_endpoints",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_deliveries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<short>(type: "smallint", nullable: false),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                partition_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_deliveries", x => x.id);
                table.ForeignKey(
                    name: "FK_hookwright_deliveries_hookwright_endpoints_endpoint_id",
                    column: x => x.endpoint_id,
                    principalTable: "hookwright_endpoints",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_hookwright_deliveries_hookwright_events_event_id",
                    column: x => x.event_id,
                    principalTable: "hookwright_events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "hookwright_delivery_attempts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                outcome = table.Column<short>(type: "smallint", nullable: false),
                attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                duration_ms = table.Column<long>(type: "bigint", nullable: false),
                response_status_code = table.Column<int>(type: "integer", nullable: true),
                response_body_snippet = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                error_detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                response_headers = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hookwright_delivery_attempts", x => x.id);
                table.ForeignKey(
                    name: "FK_hookwright_delivery_attempts_hookwright_deliveries_delivery~",
                    column: x => x.delivery_id,
                    principalTable: "hookwright_deliveries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_endpoint_id_created_at",
            table: "hookwright_deliveries",
            columns: ["endpoint_id", "created_at"],
            descending: [false, true]);

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_event_id",
            table: "hookwright_deliveries",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_deliveries_state_next_attempt_at",
            table: "hookwright_deliveries",
            columns: ["state", "next_attempt_at"]);

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_delivery_attempts_delivery_id_attempted_at",
            table: "hookwright_delivery_attempts",
            columns: ["delivery_id", "attempted_at"],
            descending: [false, true]);

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_endpoint_secrets_endpoint_id_valid_until",
            table: "hookwright_endpoint_secrets",
            columns: ["endpoint_id", "valid_until"]);

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_endpoints_subscriber_id_health",
            table: "hookwright_endpoints",
            columns: ["subscriber_id", "health"]);

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_events_created_at",
            table: "hookwright_events",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_events_idempotency_key",
            table: "hookwright_events",
            column: "idempotency_key");

        migrationBuilder.CreateIndex(
            name: "ix_hookwright_events_subscriber_id_id",
            table: "hookwright_events",
            columns: ["subscriber_id", "id"]);

        migrationBuilder.CreateIndex(
            name: "ux_hookwright_subscribers_external_id",
            table: "hookwright_subscribers",
            column: "external_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "hookwright_delivery_attempts");

        migrationBuilder.DropTable(
            name: "hookwright_endpoint_secrets");

        migrationBuilder.DropTable(
            name: "hookwright_event_types");

        migrationBuilder.DropTable(
            name: "hookwright_deliveries");

        migrationBuilder.DropTable(
            name: "hookwright_endpoints");

        migrationBuilder.DropTable(
            name: "hookwright_events");

        migrationBuilder.DropTable(
            name: "hookwright_subscribers");
    }
}
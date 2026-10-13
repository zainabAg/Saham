using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase11_OutboxReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "AppSmsLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "AppNotifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseUntil",
                table: "AppNotifications",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSmsLogs_IdempotencyKey",
                table: "AppSmsLogs",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            // Events written before the key was unique: keep every row (and its published state);
            // later duplicates of a key get a ":dup:{Id}" suffix so the index can be built.
            migrationBuilder.Sql("""
                UPDATE "AppOutboxEvents" AS e SET "EventKey" = left(e."EventKey", 140) || ':dup:' || e."Id"
                WHERE EXISTS (SELECT 1 FROM "AppOutboxEvents" AS o WHERE o."EventKey" = e."EventKey" AND o."Id" < e."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "UX_AppOutboxEvents_EventKey",
                table: "AppOutboxEvents",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_ExpiredLease",
                table: "AppNotifications",
                column: "LeaseUntil",
                filter: "\"Status\" = 'Processing'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Lease",
                table: "AppNotifications",
                sql: "(\"Status\" = 'Processing') = (\"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSmsLogs_IdempotencyKey",
                table: "AppSmsLogs");

            migrationBuilder.DropIndex(
                name: "UX_AppOutboxEvents_EventKey",
                table: "AppOutboxEvents");

            migrationBuilder.DropIndex(
                name: "IX_AppNotifications_ExpiredLease",
                table: "AppNotifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Lease",
                table: "AppNotifications");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "AppSmsLogs");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "AppNotifications");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "AppNotifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase9b_InAppCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnsweredAt",
                table: "AppContactAttempts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "AppContactAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "AppContactAttempts",
                type: "timestamp without time zone",
                nullable: true);

            // Attempts recorded before calls existed are all finished: only new in-app calls can be live.
            migrationBuilder.Sql("UPDATE \"AppContactAttempts\" SET \"EndedAt\" = \"StartedAt\" WHERE \"EndedAt\" IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_AppContactAttempts_Live",
                table: "AppContactAttempts",
                columns: new[] { "OrderId", "StartedAt" },
                filter: "\"EndedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppContactAttempts_Live",
                table: "AppContactAttempts");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "AppContactAttempts");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "AppContactAttempts");

            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "AppContactAttempts");
        }
    }
}

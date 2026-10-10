using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase0_1_Foundation_Identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "AppCustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoShowCount = table.Column<int>(type: "integer", nullable: false),
                    CashDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    CashDisabledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CashBanLiftedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CashBanLiftedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCustomerProfiles", x => x.Id);
                    table.CheckConstraint("CK_CustomerProfiles_NoShowCount", "\"NoShowCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AppIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "jsonb", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppIdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppRestaurantStaff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRestaurantStaff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSmsLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSmsLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppIdempotencyRecords_CreationTime",
                table: "AppIdempotencyRecords",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AppIdempotencyRecords_UserId_Scope_Key",
                table: "AppIdempotencyRecords",
                columns: new[] { "UserId", "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOtpChallenges_ExpiresAt",
                table: "AppOtpChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AppOtpChallenges_PhoneNumber_Purpose_CreationTime",
                table: "AppOtpChallenges",
                columns: new[] { "PhoneNumber", "Purpose", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRestaurantStaff_RestaurantId_Role",
                table: "AppRestaurantStaff",
                columns: new[] { "RestaurantId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRestaurantStaff_UserId",
                table: "AppRestaurantStaff",
                column: "UserId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppSmsLogs_CreationTime",
                table: "AppSmsLogs",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AppSmsLogs_PhoneNumber_CreationTime",
                table: "AppSmsLogs",
                columns: new[] { "PhoneNumber", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCustomerProfiles");

            migrationBuilder.DropTable(
                name: "AppIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "AppOtpChallenges");

            migrationBuilder.DropTable(
                name: "AppRestaurantStaff");

            migrationBuilder.DropTable(
                name: "AppSmsLogs");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}

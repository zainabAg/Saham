using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_Catalog_Geography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppBooks");

            migrationBuilder.CreateTable(
                name: "AppCities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Center = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    ServiceArea = table.Column<MultiPolygon>(type: "geography (multipolygon, 4326)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCustomerAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_AppCustomerAddresses", x => x.Id);
                    table.CheckConstraint("CK_CustomerAddresses_Description", "char_length(\"Description\") >= 1");
                    table.ForeignKey(
                        name: "FK_AppCustomerAddresses_AbpUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCustomerAddresses_AppCities_CityId",
                        column: x => x.CityId,
                        principalTable: "AppCities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppRestaurants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    AddressText = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PausedUntil = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DefaultPrepMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinOrderAmount = table.Column<long>(type: "bigint", nullable: false),
                    CommissionRateBp = table.Column<int>(type: "integer", nullable: false),
                    TabletLastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRestaurants", x => x.Id);
                    table.CheckConstraint("CK_Restaurants_CommissionRateBp", "\"CommissionRateBp\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_Restaurants_DefaultPrepMinutes", "\"DefaultPrepMinutes\" BETWEEN 5 AND 120");
                    table.CheckConstraint("CK_Restaurants_MinOrderAmount", "\"MinOrderAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AppRestaurants_AppCities_CityId",
                        column: x => x.CityId,
                        principalTable: "AppCities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppMenuCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMenuCategories", x => x.Id);
                    table.UniqueConstraint("AK_AppMenuCategories_Id_RestaurantId", x => new { x.Id, x.RestaurantId });
                    table.ForeignKey(
                        name: "FK_AppMenuCategories_AppRestaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "AppRestaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppRestaurantHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRestaurantHours", x => x.Id);
                    table.CheckConstraint("CK_RestaurantHours_OpensClose", "\"OpensAt\" <> \"ClosesAt\"");
                    table.ForeignKey(
                        name: "FK_AppRestaurantHours_AppRestaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "AppRestaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppMenuItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "jsonb", nullable: true),
                    Name = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMenuItems", x => x.Id);
                    table.CheckConstraint("CK_MenuItems_Price", "\"Price\" > 0");
                    table.ForeignKey(
                        name: "FK_AppMenuItems_AppMenuCategories_CategoryId_RestaurantId",
                        columns: x => new { x.CategoryId, x.RestaurantId },
                        principalTable: "AppMenuCategories",
                        principalColumns: new[] { "Id", "RestaurantId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppMenuItems_AppRestaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "AppRestaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomerAddresses_CityId",
                table: "AppCustomerAddresses",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomerAddresses_UserId",
                table: "AppCustomerAddresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMenuCategories_RestaurantId_SortOrder",
                table: "AppMenuCategories",
                columns: new[] { "RestaurantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMenuItems_CategoryId_RestaurantId",
                table: "AppMenuItems",
                columns: new[] { "CategoryId", "RestaurantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMenuItems_RestaurantId_CategoryId_SortOrder",
                table: "AppMenuItems",
                columns: new[] { "RestaurantId", "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRestaurantHours_RestaurantId_DayOfWeek",
                table: "AppRestaurantHours",
                columns: new[] { "RestaurantId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRestaurants_CityId_Status",
                table: "AppRestaurants",
                columns: new[] { "CityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRestaurants_Location",
                table: "AppRestaurants",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.AddForeignKey(
                name: "FK_AppRestaurantStaff_AppRestaurants_RestaurantId",
                table: "AppRestaurantStaff",
                column: "RestaurantId",
                principalTable: "AppRestaurants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppRestaurantStaff_AppRestaurants_RestaurantId",
                table: "AppRestaurantStaff");

            migrationBuilder.DropTable(
                name: "AppCustomerAddresses");

            migrationBuilder.DropTable(
                name: "AppMenuItems");

            migrationBuilder.DropTable(
                name: "AppRestaurantHours");

            migrationBuilder.DropTable(
                name: "AppMenuCategories");

            migrationBuilder.DropTable(
                name: "AppRestaurants");

            migrationBuilder.DropTable(
                name: "AppCities");

            migrationBuilder.CreateTable(
                name: "AppBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Price = table.Column<float>(type: "real", nullable: false),
                    PublishDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppBooks", x => x.Id);
                });
        }
    }
}

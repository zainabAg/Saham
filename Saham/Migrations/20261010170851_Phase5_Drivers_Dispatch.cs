using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_Drivers_Dispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchStartedAt",
                table: "AppOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppDriverLocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDriverLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDrivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DocsReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Availability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActiveOrderCount = table.Column<int>(type: "integer", nullable: false),
                    CashBlocked = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CashBlockedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FloatAmount = table.Column<long>(type: "bigint", nullable: false),
                    VehiclePlate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LastLocation = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    LastLocationAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastTaskCompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDrivers", x => x.Id);
                    table.CheckConstraint("CK_Drivers_ActiveOrderCount", "\"ActiveOrderCount\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_Drivers_ApprovedBy", "\"ReviewStatus\" <> 'Approved' OR \"ApprovedBy\" IS NOT NULL");
                    table.CheckConstraint("CK_Drivers_AvailableOnlyIfApproved", "\"Availability\" = 'Offline' OR \"ReviewStatus\" = 'Approved'");
                    table.CheckConstraint("CK_Drivers_FloatAmount", "\"FloatAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AppDrivers_AbpUsers_Id",
                        column: x => x.Id,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppDrivers_AppCities_CityId",
                        column: x => x.CityId,
                        principalTable: "AppCities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedTo = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppIncidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDeliveryOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNo = table.Column<int>(type: "integer", nullable: false),
                    DistanceToRestaurantM = table.Column<int>(type: "integer", nullable: false),
                    IsBatch = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OfferedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDeliveryOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDeliveryOffers_AppDrivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AppDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppDeliveryOffers_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppDriverDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDriverDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDriverDocuments_AppDrivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AppDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppDeliveryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedByType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedByUser = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsBatch = table.Column<bool>(type: "boolean", nullable: false),
                    DriverPayAmount = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ArrivedAtRestaurantAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ArrivedAtCustomerAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReleaseReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeliveryLocation = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    GeofenceFlag = table.Column<bool>(type: "boolean", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDeliveryAssignments", x => x.Id);
                    table.CheckConstraint("CK_DeliveryAssignments_Pay", "\"DriverPayAmount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AppDeliveryAssignments_AppDeliveryOffers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "AppDeliveryOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppDeliveryAssignments_AppDrivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AppDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppDeliveryAssignments_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryAssignments_ActiveByDriver",
                table: "AppDeliveryAssignments",
                column: "DriverId",
                filter: "\"Status\" IN ('Assigned', 'PickedUp')");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryAssignments_OfferId",
                table: "AppDeliveryAssignments",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryAssignments_OneActivePerOrder",
                table: "AppDeliveryAssignments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('Assigned', 'PickedUp')");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryOffers_OnePendingPerDriver",
                table: "AppDeliveryOffers",
                column: "DriverId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryOffers_OnePendingPerOrder",
                table: "AppDeliveryOffers",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryOffers_OrderId_DriverId",
                table: "AppDeliveryOffers",
                columns: new[] { "OrderId", "DriverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDeliveryOffers_PendingExpiry",
                table: "AppDeliveryOffers",
                column: "ExpiresAt",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AppDriverDocuments_OneLivePerType",
                table: "AppDriverDocuments",
                columns: new[] { "DriverId", "DocType" },
                unique: true,
                filter: "\"Status\" <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_AppDriverLocations_DriverId_RecordedAt",
                table: "AppDriverLocations",
                columns: new[] { "DriverId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDriverLocations_ReceivedAt",
                table: "AppDriverLocations",
                column: "ReceivedAt")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "IX_AppDrivers_AvailableLocation",
                table: "AppDrivers",
                column: "LastLocation",
                filter: "\"Availability\" = 'Available'")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_AppDrivers_CityId",
                table: "AppDrivers",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDrivers_ReviewStatus_Availability",
                table: "AppDrivers",
                columns: new[] { "ReviewStatus", "Availability" });

            migrationBuilder.CreateIndex(
                name: "IX_AppIncidents_OneOpenPerKey",
                table: "AppIncidents",
                column: "DedupeKey",
                unique: true,
                filter: "\"Status\" <> 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "IX_AppIncidents_Status_OpenedAt",
                table: "AppIncidents",
                columns: new[] { "Status", "OpenedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppDeliveryAssignments");

            migrationBuilder.DropTable(
                name: "AppDriverDocuments");

            migrationBuilder.DropTable(
                name: "AppDriverLocations");

            migrationBuilder.DropTable(
                name: "AppIncidents");

            migrationBuilder.DropTable(
                name: "AppDeliveryOffers");

            migrationBuilder.DropTable(
                name: "AppDrivers");

            migrationBuilder.DropColumn(
                name: "DispatchStartedAt",
                table: "AppOrders");
        }
    }
}

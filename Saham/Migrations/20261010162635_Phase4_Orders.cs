using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_Orders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "AppOrderNumbers",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "AppOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('\"AppOrderNumbers\"')"),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HoldReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DispatchStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReleaseAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeliveryLocation = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    DeliveryDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AddressLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RecipientPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DistanceM = table.Column<int>(type: "integer", nullable: false),
                    ItemsSubtotal = table.Column<long>(type: "bigint", nullable: false),
                    DiscountAmount = table.Column<long>(type: "bigint", nullable: false),
                    CompanyDiscount = table.Column<long>(type: "bigint", nullable: false),
                    RestaurantDiscount = table.Column<long>(type: "bigint", nullable: false),
                    DeliveryFeeBase = table.Column<long>(type: "bigint", nullable: false),
                    FreeDeliveryApplied = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryFeeCharged = table.Column<long>(type: "bigint", nullable: false),
                    TotalAmount = table.Column<long>(type: "bigint", nullable: false),
                    CommissionRateBp = table.Column<int>(type: "integer", nullable: false),
                    CommissionAmount = table.Column<long>(type: "bigint", nullable: false),
                    RestaurantNetAmount = table.Column<long>(type: "bigint", nullable: false),
                    DriverPayAmount = table.Column<long>(type: "bigint", nullable: false),
                    DebtCollected = table.Column<long>(type: "bigint", nullable: false),
                    CreditApplied = table.Column<long>(type: "bigint", nullable: false),
                    AmountDue = table.Column<long>(type: "bigint", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CashTendered = table.Column<long>(type: "bigint", nullable: true),
                    TravelMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrepMinutes = table.Column<int>(type: "integer", nullable: true),
                    PlacedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EstimatedReadyAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReadyAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EtaMinAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EtaMaxAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadyNotMarked = table.Column<bool>(type: "boolean", nullable: false),
                    ItemDecisionDeadlineAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CancelNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CancelledByType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrders", x => x.Id);
                    table.CheckConstraint("CK_Orders_AmountDue", "\"AmountDue\" = \"TotalAmount\" + \"DebtCollected\" - \"CreditApplied\"");
                    table.CheckConstraint("CK_Orders_CashTendered", "\"CashTendered\" IS NULL OR \"CashTendered\" >= \"AmountDue\"");
                    table.CheckConstraint("CK_Orders_Discount", "\"DiscountAmount\" = \"CompanyDiscount\" + \"RestaurantDiscount\" AND \"DiscountAmount\" <= \"ItemsSubtotal\"");
                    table.CheckConstraint("CK_Orders_HoldPausesDispatch", "\"HoldReason\" IS NULL OR \"DispatchStatus\" IN ('Paused', 'NotStarted', 'Assigned')");
                    table.CheckConstraint("CK_Orders_NonNegative", "\"ItemsSubtotal\" >= 0 AND \"CompanyDiscount\" >= 0 AND \"RestaurantDiscount\" >= 0 AND \"DeliveryFeeBase\" >= 0 AND \"DeliveryFeeCharged\" >= 0 AND \"CommissionAmount\" >= 0 AND \"RestaurantNetAmount\" >= 0 AND \"DriverPayAmount\" >= 0 AND \"DebtCollected\" >= 0 AND \"CreditApplied\" >= 0 AND \"AmountDue\" >= 0");
                    table.CheckConstraint("CK_Orders_RestaurantNet", "\"RestaurantNetAmount\" = \"ItemsSubtotal\" - \"RestaurantDiscount\" - \"CommissionAmount\"");
                    table.CheckConstraint("CK_Orders_Total", "\"TotalAmount\" = \"ItemsSubtotal\" - \"DiscountAmount\" + \"DeliveryFeeCharged\"");
                    table.ForeignKey(
                        name: "FK_AppOrders_AbpUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrders_AppCities_CityId",
                        column: x => x.CityId,
                        principalTable: "AppCities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrders_AppPricingPlans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "AppPricingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrders_AppQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "AppQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrders_AppRestaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "AppRestaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppOrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    LineTotal = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsReportedUnavailable = table.Column<bool>(type: "boolean", nullable: false),
                    RemovedReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NameSnapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Amounts", "\"Quantity\" > 0 AND \"UnitPrice\" > 0 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\"");
                    table.ForeignKey(
                        name: "FK_AppOrderItems_AppMenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "AppMenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrderItems_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppOrderModifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModificationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Before = table.Column<string>(type: "jsonb", nullable: false),
                    After = table.Column<string>(type: "jsonb", nullable: false),
                    AmountDelta = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrderModifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppOrderModifications_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClientOccurredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppOrderStatusHistory_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderItems_MenuItemId",
                table: "AppOrderItems",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderItems_OrderId",
                table: "AppOrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderModifications_OrderId",
                table: "AppOrderModifications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_CityId",
                table: "AppOrders",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_CustomerId_CreationTime",
                table: "AppOrders",
                columns: new[] { "CustomerId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_ItemDecision",
                table: "AppOrders",
                column: "ItemDecisionDeadlineAt",
                filter: "\"HoldReason\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_Live",
                table: "AppOrders",
                columns: new[] { "Status", "CreationTime" },
                filter: "\"Status\" NOT IN ('Delivered', 'Cancelled', 'DeliveryFailed')");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_Number",
                table: "AppOrders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_PricingPlanId",
                table: "AppOrders",
                column: "PricingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_QuoteId",
                table: "AppOrders",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_RestaurantId_Status_CreationTime",
                table: "AppOrders",
                columns: new[] { "RestaurantId", "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_ScheduledRelease",
                table: "AppOrders",
                column: "ReleaseAt",
                filter: "\"Status\" = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderStatusHistory_OrderId_OccurredAt",
                table: "AppOrderStatusHistory",
                columns: new[] { "OrderId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppOrderItems");

            migrationBuilder.DropTable(
                name: "AppOrderModifications");

            migrationBuilder.DropTable(
                name: "AppOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "AppOrders");

            migrationBuilder.DropSequence(
                name: "AppOrderNumbers");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_Pricing_Quotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppPricingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DriverBasePay = table.Column<long>(type: "bigint", nullable: false),
                    BatchSecondOrderPay = table.Column<long>(type: "bigint", nullable: false),
                    CancelAfterArrivalPayPct = table.Column<int>(type: "integer", nullable: false),
                    FreeDeliveryThreshold = table.Column<long>(type: "bigint", nullable: false),
                    FreeDeliveryOperator = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FreeDeliveryBasis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DistanceMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaxDeliveryDistanceM = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPricingPlans", x => x.Id);
                    table.CheckConstraint("CK_PricingPlans_Amounts", "\"DriverBasePay\" >= 0 AND \"BatchSecondOrderPay\" >= 0 AND \"FreeDeliveryThreshold\" >= 0 AND \"MaxDeliveryDistanceM\" > 0 AND \"CancelAfterArrivalPayPct\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_AppPricingPlans_AppCities_CityId",
                        column: x => x.CityId,
                        principalTable: "AppCities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppPricingTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromM = table.Column<int>(type: "integer", nullable: false),
                    ToM = table.Column<int>(type: "integer", nullable: false),
                    CustomerFee = table.Column<long>(type: "bigint", nullable: false),
                    DriverExtra = table.Column<long>(type: "bigint", nullable: false),
                    FreeDeliveryEligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPricingTiers", x => x.Id);
                    table.CheckConstraint("CK_PricingTiers_Amounts", "\"CustomerFee\" >= 0 AND \"DriverExtra\" >= 0");
                    table.CheckConstraint("CK_PricingTiers_Range", "\"ToM\" > \"FromM\" AND \"FromM\" >= 0");
                    table.ForeignKey(
                        name: "FK_AppPricingTiers_AppPricingPlans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "AppPricingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AmountDue = table.Column<long>(type: "bigint", nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ConsumedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppQuotes", x => x.Id);
                    table.CheckConstraint("CK_Quotes_AmountDue", "\"AmountDue\" >= 0");
                    table.ForeignKey(
                        name: "FK_AppQuotes_AbpUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppQuotes_AppCustomerAddresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "AppCustomerAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppQuotes_AppPricingPlans_PricingPlanId",
                        column: x => x.PricingPlanId,
                        principalTable: "AppPricingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppQuotes_AppRestaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "AppRestaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppPricingPlans_CityId_Active",
                table: "AppPricingPlans",
                column: "CityId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AppPricingTiers_PricingPlanId_FromM",
                table: "AppPricingTiers",
                columns: new[] { "PricingPlanId", "FromM" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_AddressId",
                table: "AppQuotes",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_ConsumedOrderId",
                table: "AppQuotes",
                column: "ConsumedOrderId",
                unique: true,
                filter: "\"ConsumedOrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_CustomerId_CreationTime",
                table: "AppQuotes",
                columns: new[] { "CustomerId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_ExpiresAt",
                table: "AppQuotes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_PricingPlanId",
                table: "AppQuotes",
                column: "PricingPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AppQuotes_RestaurantId",
                table: "AppQuotes",
                column: "RestaurantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppPricingTiers");

            migrationBuilder.DropTable(
                name: "AppQuotes");

            migrationBuilder.DropTable(
                name: "AppPricingPlans");
        }
    }
}

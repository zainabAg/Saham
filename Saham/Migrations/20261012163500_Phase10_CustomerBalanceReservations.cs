using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_CustomerBalanceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCustomerBalanceReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditApplied = table.Column<long>(type: "bigint", nullable: false),
                    DebtCollected = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FinalizeReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCustomerBalanceReservations", x => x.Id);
                    table.CheckConstraint("CK_CustomerBalanceReservations_Finalized", "(\"Status\" = 'Active') = (\"FinalizedAt\" IS NULL)");
                    table.CheckConstraint("CK_CustomerBalanceReservations_NonNegative", "\"CreditApplied\" >= 0 AND \"DebtCollected\" >= 0");
                    table.CheckConstraint("CK_CustomerBalanceReservations_OneKind", "NOT (\"CreditApplied\" > 0 AND \"DebtCollected\" > 0)");
                    table.ForeignKey(
                        name: "FK_AppCustomerBalanceReservations_AbpUsers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCustomerBalanceReservations_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomerBalanceReservations_ActiveByCustomer",
                table: "AppCustomerBalanceReservations",
                column: "CustomerId",
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomerBalanceReservations_OrderId",
                table: "AppCustomerBalanceReservations",
                column: "OrderId",
                unique: true);

            // Open orders from before this phase keep their promised credit/debt as Active holds.
            migrationBuilder.Sql(Saham.Data.Configurations.CustomerBalanceReservationSql.BackfillOpenOrders);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCustomerBalanceReservations");
        }
    }
}

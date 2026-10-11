using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_Pickup_Delivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryOtpHash",
                table: "AppOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryOtpProtected",
                table: "AppOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandedOverBy",
                table: "AppOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtpFailedAttempts",
                table: "AppOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "OtpLocked",
                table: "AppOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideConfirmedBy",
                table: "AppOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "AppOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProofMethod",
                table: "AppOrders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestaurantHandoverAt",
                table: "AppOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PickedUpByType",
                table: "AppDeliveryAssignments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryOtpHash",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "DeliveryOtpProtected",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "HandedOverBy",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "OtpFailedAttempts",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "OtpLocked",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "OverrideConfirmedBy",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "ProofMethod",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "RestaurantHandoverAt",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "PickedUpByType",
                table: "AppDeliveryAssignments");
        }
    }
}

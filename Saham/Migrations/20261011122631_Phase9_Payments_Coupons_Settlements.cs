using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_Payments_Coupons_Settlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelChargedTo",
                table: "AppOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "AppOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponDiscountType",
                table: "AppOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponFundedBy",
                table: "AppOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CouponId",
                table: "AppOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CouponMaxDiscount",
                table: "AppOrders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CouponValue",
                table: "AppOrders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ItemDecisionEscalatedAt",
                table: "AppOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubstituteForItemId",
                table: "AppOrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppCouponRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCouponRedemptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCoupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    MaxDiscount = table.Column<long>(type: "bigint", nullable: true),
                    MinSubtotal = table.Column<long>(type: "bigint", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UsageLimitTotal = table.Column<int>(type: "integer", nullable: true),
                    UsageLimitPerCustomer = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    FundedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StackableWithFreeDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppCoupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_ActiveApproved", "\"Status\" <> 'Active' OR \"ApprovedBy\" IS NOT NULL");
                    table.CheckConstraint("CK_Coupons_Percent", "\"DiscountType\" <> 'Percent' OR \"Value\" BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Coupons_RestaurantFunded", "\"FundedBy\" <> 'Restaurant' OR \"RestaurantId\" IS NOT NULL");
                    table.CheckConstraint("CK_Coupons_UsedCount", "\"UsedCount\" >= 0 AND (\"UsageLimitTotal\" IS NULL OR \"UsedCount\" <= \"UsageLimitTotal\")");
                });

            migrationBuilder.CreateTable(
                name: "AppPaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    SignatureValid = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPaymentEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    CapturedAmount = table.Column<long>(type: "bigint", nullable: false),
                    RefundedAmount = table.Column<long>(type: "bigint", nullable: false),
                    ProviderTxnId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RedirectUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                    table.PrimaryKey("PK_AppPayments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amounts", "\"Amount\" > 0 AND \"CapturedAmount\" >= 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"CapturedAmount\"");
                });

            migrationBuilder.CreateTable(
                name: "AppRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolutionChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
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
                    table.PrimaryKey("PK_AppRefunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amount", "\"Amount\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "AppSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PartyType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningBalance = table.Column<long>(type: "bigint", nullable: false),
                    TotalDebits = table.Column<long>(type: "bigint", nullable: false),
                    TotalCredits = table.Column<long>(type: "bigint", nullable: false),
                    NetAmount = table.Column<long>(type: "bigint", nullable: false),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfirmedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PaidReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaidChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PaidBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PayoutEntryId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_AppSettlements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrders_CouponId",
                table: "AppOrders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderItems_SubstituteForItemId",
                table: "AppOrderItems",
                column: "SubstituteForItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCouponRedemptions_CouponId_CustomerId_Status",
                table: "AppCouponRedemptions",
                columns: new[] { "CouponId", "CustomerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCouponRedemptions_OrderId",
                table: "AppCouponRedemptions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCoupons_Code",
                table: "AppCoupons",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppCoupons_Status_RestaurantId",
                table: "AppCoupons",
                columns: new[] { "Status", "RestaurantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPaymentEvents_PaymentId",
                table: "AppPaymentEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPaymentEvents_Provider_ProviderEventId",
                table: "AppPaymentEvents",
                columns: new[] { "Provider", "ProviderEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPayments_IdempotencyKey",
                table: "AppPayments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPayments_OneLivePerOrder",
                table: "AppPayments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" IN ('Initiated', 'Pending', 'Captured') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AppPayments_Provider_ProviderTxnId",
                table: "AppPayments",
                columns: new[] { "Provider", "ProviderTxnId" },
                unique: true,
                filter: "\"ProviderTxnId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppPayments_Status_ExpiresAt",
                table: "AppPayments",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppRefunds_IdempotencyKey",
                table: "AppRefunds",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRefunds_OrderId",
                table: "AppRefunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppRefunds_ProviderRefundId",
                table: "AppRefunds",
                column: "ProviderRefundId",
                unique: true,
                filter: "\"ProviderRefundId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppRefunds_Status_NextAttemptAt",
                table: "AppRefunds",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettlements_SettlementType_PartyType_PartyId_PeriodStart",
                table: "AppSettlements",
                columns: new[] { "SettlementType", "PartyType", "PartyId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettlements_Status_SettlementType",
                table: "AppSettlements",
                columns: new[] { "Status", "SettlementType" });

            migrationBuilder.Sql(Saham.Data.Configurations.SettlementSql.CreateTriggers);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Saham.Data.Configurations.SettlementSql.DropTriggers);

            migrationBuilder.DropTable(
                name: "AppCouponRedemptions");

            migrationBuilder.DropTable(
                name: "AppCoupons");

            migrationBuilder.DropTable(
                name: "AppPaymentEvents");

            migrationBuilder.DropTable(
                name: "AppPayments");

            migrationBuilder.DropTable(
                name: "AppRefunds");

            migrationBuilder.DropTable(
                name: "AppSettlements");

            migrationBuilder.DropIndex(
                name: "IX_AppOrders_CouponId",
                table: "AppOrders");

            migrationBuilder.DropIndex(
                name: "IX_AppOrderItems_SubstituteForItemId",
                table: "AppOrderItems");

            migrationBuilder.DropColumn(
                name: "CancelChargedTo",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponDiscountType",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponFundedBy",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponId",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponMaxDiscount",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "CouponValue",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "ItemDecisionEscalatedAt",
                table: "AppOrders");

            migrationBuilder.DropColumn(
                name: "SubstituteForItemId",
                table: "AppOrderItems");
        }
    }
}

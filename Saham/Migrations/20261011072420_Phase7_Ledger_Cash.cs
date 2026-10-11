using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saham.Migrations
{
    /// <inheritdoc />
    public partial class Phase7_Ledger_Cash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChangeShortfall",
                table: "AppOrders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AppContactAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppContactAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppContactAttempts_AppDeliveryAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "AppDeliveryAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppContactAttempts_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppJournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppJournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppJournalEntries_AppJournalEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "AppJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppLedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PartyType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedAmount = table.Column<long>(type: "bigint", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCashMovements", x => x.Id);
                    table.CheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_AppCashMovements_AppDrivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "AppDrivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppCashMovements_AppJournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "AppJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppJournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit = table.Column<long>(type: "bigint", nullable: false),
                    Credit = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppJournalLines", x => x.Id);
                    table.CheckConstraint("CK_JournalLines_OneSided", "(\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0)");
                    table.ForeignKey(
                        name: "FK_AppJournalLines_AppJournalEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "AppJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppJournalLines_AppLedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AppLedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppJournalLines_AppOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "AppOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCashMovements_DriverId_BusinessDate",
                table: "AppCashMovements",
                columns: new[] { "DriverId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AppCashMovements_JournalEntryId",
                table: "AppCashMovements",
                column: "JournalEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppContactAttempts_AssignmentId",
                table: "AppContactAttempts",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppContactAttempts_OrderId",
                table: "AppContactAttempts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalEntries_BusinessDate",
                table: "AppJournalEntries",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalEntries_IdempotencyKey",
                table: "AppJournalEntries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalEntries_ReversesEntryId",
                table: "AppJournalEntries",
                column: "ReversesEntryId",
                unique: true,
                filter: "\"ReversesEntryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalEntries_SourceType_SourceId",
                table: "AppJournalEntries",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalLines_AccountId",
                table: "AppJournalLines",
                column: "AccountId")
                .Annotation("Npgsql:IndexInclude", new[] { "Debit", "Credit" });

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalLines_AccountId_SettlementId",
                table: "AppJournalLines",
                columns: new[] { "AccountId", "SettlementId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalLines_EntryId",
                table: "AppJournalLines",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AppJournalLines_OrderId",
                table: "AppJournalLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AppLedgerAccounts_Code_PartyType_PartyId",
                table: "AppLedgerAccounts",
                columns: new[] { "Code", "PartyType", "PartyId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            // Ledger immutability and balance (SRS 13.8 §4-5, SOD-04)
            migrationBuilder.Sql(Saham.Data.Configurations.LedgerSql.CreateTriggers);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Saham.Data.Configurations.LedgerSql.DropTriggers);

            migrationBuilder.DropTable(
                name: "AppCashMovements");

            migrationBuilder.DropTable(
                name: "AppContactAttempts");

            migrationBuilder.DropTable(
                name: "AppJournalLines");

            migrationBuilder.DropTable(
                name: "AppJournalEntries");

            migrationBuilder.DropTable(
                name: "AppLedgerAccounts");

            migrationBuilder.DropColumn(
                name: "ChangeShortfall",
                table: "AppOrders");
        }
    }
}

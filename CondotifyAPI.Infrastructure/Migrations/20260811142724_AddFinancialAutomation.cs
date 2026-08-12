using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    ImportedRows = table.Column<int>(type: "integer", nullable: false),
                    InvalidRows = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialImportBatches_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialRecurringRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AllUnits = table.Column<bool>(type: "boolean", nullable: false),
                    GenerationDay = table.Column<int>(type: "integer", nullable: false),
                    DueDay = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReferenceTemplate = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FineAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastGeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialRecurringRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialRecurringRules_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DeliveryKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    DestinationLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialReminderDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialReminderDeliveries_FinancialCharges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "FinancialCharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialReminderDeliveries_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialReminderDeliveries_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialReminderPolicies",
                columns: table => new
                {
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PushEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BeforeDueDays = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OnDueDate = table.Column<bool>(type: "boolean", nullable: false),
                    FirstOverdueDay = table.Column<int>(type: "integer", nullable: false),
                    RepeatEveryDays = table.Column<int>(type: "integer", nullable: false),
                    MaxOverdueDays = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialReminderPolicies", x => x.LicenseId);
                    table.ForeignKey(
                        name: "FK_FinancialReminderPolicies_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinancialRecurringRuleUnits",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialRecurringRuleUnits", x => new { x.RuleId, x.UnitId });
                    table.ForeignKey(
                        name: "FK_FinancialRecurringRuleUnits_FinancialRecurringRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "FinancialRecurringRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialRecurringRuleUnits_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialRecurringRuleUnits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportBatches_LicenseId_CreatedAt",
                table: "FinancialImportBatches",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialImportBatches_LicenseId_IdempotencyKey",
                table: "FinancialImportBatches",
                columns: new[] { "LicenseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialRecurringRules_LicenseId_IsActive_NextRunAt",
                table: "FinancialRecurringRules",
                columns: new[] { "LicenseId", "IsActive", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialRecurringRules_LicenseId_Name",
                table: "FinancialRecurringRules",
                columns: new[] { "LicenseId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialRecurringRuleUnits_LicenseId_UnitId",
                table: "FinancialRecurringRuleUnits",
                columns: new[] { "LicenseId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialRecurringRuleUnits_UnitId",
                table: "FinancialRecurringRuleUnits",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReminderDeliveries_ChargeId_CreatedAt",
                table: "FinancialReminderDeliveries",
                columns: new[] { "ChargeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReminderDeliveries_LicenseId_DeliveryKey",
                table: "FinancialReminderDeliveries",
                columns: new[] { "LicenseId", "DeliveryKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReminderDeliveries_ResidentId_FinishedAt",
                table: "FinancialReminderDeliveries",
                columns: new[] { "ResidentId", "FinishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReminderDeliveries_Status_NextAttemptAt_CreatedAt",
                table: "FinancialReminderDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialImportBatches");

            migrationBuilder.DropTable(
                name: "FinancialRecurringRuleUnits");

            migrationBuilder.DropTable(
                name: "FinancialReminderDeliveries");

            migrationBuilder.DropTable(
                name: "FinancialReminderPolicies");

            migrationBuilder.DropTable(
                name: "FinancialRecurringRules");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoletoDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Competence = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FineAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialCharges_BoletoDocuments_BoletoDocumentId",
                        column: x => x.BoletoDocumentId,
                        principalTable: "BoletoDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FinancialCharges_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialCharges_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialChargeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialChargeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialChargeEvents_FinancialCharges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "FinancialCharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialChargeEvents_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialChargeEvents_ChargeId_CreatedAt",
                table: "FinancialChargeEvents",
                columns: new[] { "ChargeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialChargeEvents_LicenseId_CreatedAt",
                table: "FinancialChargeEvents",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_BoletoDocumentId",
                table: "FinancialCharges",
                column: "BoletoDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_LicenseId_RequestKey",
                table: "FinancialCharges",
                columns: new[] { "LicenseId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_LicenseId_Status_DueDate",
                table: "FinancialCharges",
                columns: new[] { "LicenseId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_LicenseId_UnitId_DueDate",
                table: "FinancialCharges",
                columns: new[] { "LicenseId", "UnitId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCharges_UnitId",
                table: "FinancialCharges",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialChargeEvents");

            migrationBuilder.DropTable(
                name: "FinancialCharges");
        }
    }
}

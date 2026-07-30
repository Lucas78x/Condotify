using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsConditionActive = table.Column<bool>(type: "boolean", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    FirstOccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastOccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AcknowledgementNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationalAlerts_Enterprise_EnterpriseId",
                        column: x => x.EnterpriseId,
                        principalTable: "Enterprise",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OperationalAlerts_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlerts_EnterpriseId_Fingerprint",
                table: "OperationalAlerts",
                columns: new[] { "EnterpriseId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlerts_EnterpriseId_Status_Severity_LastOccurred~",
                table: "OperationalAlerts",
                columns: new[] { "EnterpriseId", "Status", "Severity", "LastOccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalAlerts_LicenseId_Status_LastOccurredAt",
                table: "OperationalAlerts",
                columns: new[] { "LicenseId", "Status", "LastOccurredAt" });

            migrationBuilder.Sql(
                """
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 2097152
                WHERE ("Permissions" & 1) = 1;

                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 4194304
                WHERE "Role" IN (0, 1, 2) AND "IsActive" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" & ~6291456;
                """);

            migrationBuilder.DropTable(
                name: "OperationalAlerts");
        }
    }
}

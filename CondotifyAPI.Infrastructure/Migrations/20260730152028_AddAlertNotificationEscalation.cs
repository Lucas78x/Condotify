using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertNotificationEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EscalationLevel",
                table: "OperationalAlerts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotifiedAt",
                table: "OperationalAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextEscalationAt",
                table: "OperationalAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AlertNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EscalationLevel = table.Column<int>(type: "integer", nullable: false),
                    DeliveryKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    DestinationLabel = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertNotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AlertNotificationDeliveries_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlertNotificationDeliveries_OperationalAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "OperationalAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AlertNotificationPolicies",
                columns: table => new
                {
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumSeverity = table.Column<int>(type: "integer", nullable: false),
                    WarningSlaMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    CriticalSlaMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15),
                    EscalationRepeatMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    WebhookEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookUrl = table.Column<string>(type: "text", nullable: false),
                    WebhookSecret = table.Column<string>(type: "text", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EmailRecipients = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertNotificationPolicies", x => x.LicenseId);
                    table.ForeignKey(
                        name: "FK_AlertNotificationPolicies_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationDeliveries_AlertId",
                table: "AlertNotificationDeliveries",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationDeliveries_DeliveryKey",
                table: "AlertNotificationDeliveries",
                column: "DeliveryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationDeliveries_LicenseId_CreatedAt",
                table: "AlertNotificationDeliveries",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotificationDeliveries_Status_NextAttemptAt_LeaseExpir~",
                table: "AlertNotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "EscalationLevel",
                table: "OperationalAlerts");

            migrationBuilder.DropColumn(
                name: "LastNotifiedAt",
                table: "OperationalAlerts");

            migrationBuilder.DropColumn(
                name: "NextEscalationAt",
                table: "OperationalAlerts");
        }
    }
}

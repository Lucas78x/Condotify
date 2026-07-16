using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAccessControlOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "ResidentAccessDevices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastErrorAt",
                table: "ResidentAccessDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSuccessAt",
                table: "ResidentAccessDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "ResidentAccessDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PortalNumbers",
                table: "ResidentAccessDevices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RouteNames",
                table: "ResidentAccessDevices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SyncStatus",
                table: "ResidentAccessDevices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxUses",
                table: "ResidentAccessCredentials",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UseCount",
                table: "ResidentAccessCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CapacityJson",
                table: "AccessControlDevices",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "DiscoveredPortalsJson",
                table: "AccessControlDevices",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "HealthMessage",
                table: "AccessControlDevices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthCheckAt",
                table: "AccessControlDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastResponseTimeMs",
                table: "AccessControlDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "AccessControlDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessBatchOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    ProcessedItems = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    FilterJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessBatchOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessBatchOperations_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessOperationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessOperationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessOperationAudits_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessRouteResidentOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRouteResidentOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessRouteResidentOverrides_AccessRoutes_AccessRouteId",
                        column: x => x.AccessRouteId,
                        principalTable: "AccessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessRouteResidentOverrides_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccessDevices_DeviceId",
                table: "ResidentAccessDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccessDevices_SyncStatus_NextAttemptAt",
                table: "ResidentAccessDevices",
                columns: new[] { "SyncStatus", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_LicenseId",
                table: "AccessBatchOperations",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_Status_CreatedAt",
                table: "AccessBatchOperations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationAudits_EntityType_EntityId",
                table: "AccessOperationAudits",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationAudits_LicenseId_CreatedAt",
                table: "AccessOperationAudits",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRouteResidentOverrides_AccessRouteId_ResidentId",
                table: "AccessRouteResidentOverrides",
                columns: new[] { "AccessRouteId", "ResidentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRouteResidentOverrides_ResidentId",
                table: "AccessRouteResidentOverrides",
                column: "ResidentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResidentAccessDevices_AccessControlDevices_DeviceId",
                table: "ResidentAccessDevices",
                column: "DeviceId",
                principalTable: "AccessControlDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResidentAccessDevices_AccessControlDevices_DeviceId",
                table: "ResidentAccessDevices");

            migrationBuilder.DropTable(
                name: "AccessBatchOperations");

            migrationBuilder.DropTable(
                name: "AccessOperationAudits");

            migrationBuilder.DropTable(
                name: "AccessRouteResidentOverrides");

            migrationBuilder.DropIndex(
                name: "IX_ResidentAccessDevices_DeviceId",
                table: "ResidentAccessDevices");

            migrationBuilder.DropIndex(
                name: "IX_ResidentAccessDevices_SyncStatus_NextAttemptAt",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "LastErrorAt",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "LastSuccessAt",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "PortalNumbers",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "RouteNames",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                table: "ResidentAccessDevices");

            migrationBuilder.DropColumn(
                name: "MaxUses",
                table: "ResidentAccessCredentials");

            migrationBuilder.DropColumn(
                name: "UseCount",
                table: "ResidentAccessCredentials");

            migrationBuilder.DropColumn(
                name: "CapacityJson",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "DiscoveredPortalsJson",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "HealthMessage",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckAt",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LastResponseTimeMs",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "AccessControlDevices");
        }
    }
}

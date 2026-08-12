using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfflineAccessDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Platform = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DeviceSecret = table.Column<string>(type: "text", nullable: false),
                    OfflineWindowMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 480),
                    IsPrimaryValidator = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastBundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastBundleGeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastBundleExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineAccessDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfflineAccessDevices_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfflineAccessDevices_UserAccess_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccess",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfflineAccessOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AfterStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineAccessOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfflineAccessOperations_AccessVisits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "AccessVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfflineAccessOperations_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfflineAccessOperations_OfflineAccessDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "OfflineAccessDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessDevices_LicenseId_InstallationId",
                table: "OfflineAccessDevices",
                columns: new[] { "LicenseId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessDevices_LicenseId_Status_LastSyncedAt",
                table: "OfflineAccessDevices",
                columns: new[] { "LicenseId", "Status", "LastSyncedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessDevices_UserId",
                table: "OfflineAccessDevices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessOperations_DeviceId_ClientOperationId",
                table: "OfflineAccessOperations",
                columns: new[] { "DeviceId", "ClientOperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessOperations_LicenseId_ReceivedAt",
                table: "OfflineAccessOperations",
                columns: new[] { "LicenseId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessOperations_LicenseId_Status_ReceivedAt",
                table: "OfflineAccessOperations",
                columns: new[] { "LicenseId", "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OfflineAccessOperations_VisitId",
                table: "OfflineAccessOperations",
                column: "VisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfflineAccessOperations");

            migrationBuilder.DropTable(
                name: "OfflineAccessDevices");
        }
    }
}

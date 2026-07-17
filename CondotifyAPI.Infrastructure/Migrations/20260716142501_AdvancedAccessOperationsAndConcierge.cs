using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedAccessOperationsAndConcierge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessBatchOperations_LicenseId",
                table: "AccessBatchOperations");

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "CFTVDevices",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "AccessControlDevices",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "AccessBatchOperations",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "AccessBatchOperations"
                SET "IdempotencyKey" = md5(random()::text || clock_timestamp()::text || "Id"::text)
                WHERE "IdempotencyKey" = '';
                """);

            migrationBuilder.CreateTable(
                name: "AccessInventoryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    RemoteKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ExternalCredentialId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CredentialType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RemoteActive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessInventoryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessInventoryItems_AccessControlDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "AccessControlDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessInventoryItems_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessInventoryItems_ResidentAccessCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "ResidentAccessCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AccessOperationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessOperationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessOperationItems_AccessBatchOperations_BatchId",
                        column: x => x.BatchId,
                        principalTable: "AccessBatchOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessOperationItems_AccessControlDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "AccessControlDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AccessOperationItems_ResidentAccessCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "ResidentAccessCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AccessVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuestResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Document = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Company = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    VehiclePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PhotoUrl = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckedInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessVisits_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessVisits_ResidentAccessCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "ResidentAccessCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessVisits_Resident_GuestResidentId",
                        column: x => x.GuestResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessVisits_Resident_HostResidentId",
                        column: x => x.HostResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_LicenseId_IdempotencyKey",
                table: "AccessBatchOperations",
                columns: new[] { "LicenseId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessInventoryItems_CredentialId",
                table: "AccessInventoryItems",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessInventoryItems_DeviceId_RemoteKey",
                table: "AccessInventoryItems",
                columns: new[] { "DeviceId", "RemoteKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessInventoryItems_LicenseId_Status",
                table: "AccessInventoryItems",
                columns: new[] { "LicenseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationItems_BatchId_DeviceId",
                table: "AccessOperationItems",
                columns: new[] { "BatchId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationItems_CredentialId",
                table: "AccessOperationItems",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationItems_DeviceId",
                table: "AccessOperationItems",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationItems_IdempotencyKey",
                table: "AccessOperationItems",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessOperationItems_Status_NextAttemptAt",
                table: "AccessOperationItems",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessVisits_CredentialId",
                table: "AccessVisits",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessVisits_GuestResidentId",
                table: "AccessVisits",
                column: "GuestResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessVisits_HostResidentId",
                table: "AccessVisits",
                column: "HostResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessVisits_LicenseId_Status_ValidFrom",
                table: "AccessVisits",
                columns: new[] { "LicenseId", "Status", "ValidFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessInventoryItems");

            migrationBuilder.DropTable(
                name: "AccessOperationItems");

            migrationBuilder.DropTable(
                name: "AccessVisits");

            migrationBuilder.DropIndex(
                name: "IX_AccessBatchOperations_LicenseId_IdempotencyKey",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "AccessBatchOperations");

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "CFTVDevices",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "AccessControlDevices",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_LicenseId",
                table: "AccessBatchOperations",
                column: "LicenseId");
        }
    }
}

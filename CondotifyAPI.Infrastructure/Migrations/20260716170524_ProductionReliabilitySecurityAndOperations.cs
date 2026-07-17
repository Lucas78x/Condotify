using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductionReliabilitySecurityAndOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessBatchOperations_Status_CreatedAt",
                table: "AccessBatchOperations");

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaChallengeExpiresAt",
                table: "UserAccess",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaChallengeHash",
                table: "UserAccess",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "UserAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryCodeHashesJson",
                table: "UserAccess",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                table: "UserAccess",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalNotes",
                table: "AccessVisits",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ApprovalRequired",
                table: "AccessVisits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AccessVisits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "AccessVisits",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedCheckoutAt",
                table: "AccessVisits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverstayAcknowledgedAt",
                table: "AccessVisits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceCount",
                table: "AccessVisits",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceGroupId",
                table: "AccessVisits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceSequence",
                table: "AccessVisits",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "AccessBatchOperations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAt",
                table: "AccessBatchOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "AccessBatchOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "AccessBatchOperations",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "AccessBatchOperations",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "AccessBatchOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "AccessBatchOperations",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.CreateTable(
                name: "AccessWatchlistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Document = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VehiclePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessWatchlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessWatchlistEntries_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_Status_NextAttemptAt_Priority_Created~",
                table: "AccessBatchOperations",
                columns: new[] { "Status", "NextAttemptAt", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessWatchlistEntries_LicenseId_IsActive_Document",
                table: "AccessWatchlistEntries",
                columns: new[] { "LicenseId", "IsActive", "Document" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessWatchlistEntries_LicenseId_IsActive_VehiclePlate",
                table: "AccessWatchlistEntries",
                columns: new[] { "LicenseId", "IsActive", "VehiclePlate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessWatchlistEntries");

            migrationBuilder.DropIndex(
                name: "IX_AccessBatchOperations_Status_NextAttemptAt_Priority_Created~",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "MfaChallengeExpiresAt",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "MfaChallengeHash",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryCodeHashesJson",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "ApprovalNotes",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "ApprovalRequired",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "ExpectedCheckoutAt",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "OverstayAcknowledgedAt",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "RecurrenceCount",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "RecurrenceGroupId",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "RecurrenceSequence",
                table: "AccessVisits");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "AccessBatchOperations");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "AccessBatchOperations");

            migrationBuilder.CreateIndex(
                name: "IX_AccessBatchOperations_Status_CreatedAt",
                table: "AccessBatchOperations",
                columns: new[] { "Status", "CreatedAt" });
        }
    }
}

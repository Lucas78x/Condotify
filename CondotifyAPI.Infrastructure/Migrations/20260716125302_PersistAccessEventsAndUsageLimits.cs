using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistAccessEventsAndUsageLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessEventRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Event = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Authorized = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PersonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Credential = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Portal = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessEventRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessEventRecords_AccessControlDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "AccessControlDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessEventRecords_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessEventRecords_ResidentAccessCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "ResidentAccessCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessEventRecords_CredentialId",
                table: "AccessEventRecords",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessEventRecords_DeviceId_ExternalEventId",
                table: "AccessEventRecords",
                columns: new[] { "DeviceId", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessEventRecords_LicenseId_OccurredAt",
                table: "AccessEventRecords",
                columns: new[] { "LicenseId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessEventRecords");
        }
    }
}

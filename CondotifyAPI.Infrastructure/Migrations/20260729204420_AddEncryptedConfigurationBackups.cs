using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedConfigurationBackups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfigurationBackups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceCount = table.Column<int>(type: "integer", nullable: false),
                    RouteCount = table.Column<int>(type: "integer", nullable: false),
                    CredentialCount = table.Column<int>(type: "integer", nullable: false),
                    BindingCount = table.Column<int>(type: "integer", nullable: false),
                    OverrideCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    LastRestoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRestoredBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationBackups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfigurationBackups_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationBackups_LicenseId_CreatedAt",
                table: "ConfigurationBackups",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationBackups_LicenseId_Version",
                table: "ConfigurationBackups",
                columns: new[] { "LicenseId", "Version" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 1572864
                WHERE "Role" IN (0, 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" & ~1572864;
                """);

            migrationBuilder.DropTable(
                name: "ConfigurationBackups");
        }
    }
}

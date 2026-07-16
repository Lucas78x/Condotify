using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseAccessPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccess_CPF",
                table: "UserAccess");

            migrationBuilder.DropIndex(
                name: "IX_UserAccess_PhoneNumber",
                table: "UserAccess");

            migrationBuilder.DropIndex(
                name: "IX_UserAccess_RG",
                table: "UserAccess");

            migrationBuilder.AddColumn<bool>(
                name: "IsTemporary",
                table: "ResidentAccessCredentials",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxRenewals",
                table: "ResidentAccessCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RenewalCount",
                table: "ResidentAccessCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LicenseCredentialPolicies",
                columns: table => new
                {
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    QrCodeValidityMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 1440),
                    AllowQrCodeRenewal = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MaxQrCodeRenewals = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    QrCodeRenewalMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 1440),
                    TemporaryFaceValidityMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 1440),
                    MaxTemporaryFaceValidityMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 10080),
                    RequireFacePhoto = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AutoDeactivateExpiredCredentials = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RemoveExpiredCredentialsFromDevices = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseCredentialPolicies", x => x.LicenseId);
                    table.ForeignKey(
                        name: "FK_LicenseCredentialPolicies_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicenseUserAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Permissions = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseUserAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseUserAccesses_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LicenseUserAccesses_UserAccess_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccess",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_CPF",
                table: "UserAccess",
                column: "CPF",
                unique: true,
                filter: "\"CPF\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_PhoneNumber",
                table: "UserAccess",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_RG",
                table: "UserAccess",
                column: "RG",
                unique: true,
                filter: "\"RG\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseUserAccesses_LicenseId_UserId",
                table: "LicenseUserAccesses",
                columns: new[] { "LicenseId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseUserAccesses_UserId_IsActive",
                table: "LicenseUserAccesses",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseCredentialPolicies");

            migrationBuilder.DropTable(
                name: "LicenseUserAccesses");

            migrationBuilder.DropIndex(
                name: "IX_UserAccess_CPF",
                table: "UserAccess");

            migrationBuilder.DropIndex(
                name: "IX_UserAccess_PhoneNumber",
                table: "UserAccess");

            migrationBuilder.DropIndex(
                name: "IX_UserAccess_RG",
                table: "UserAccess");

            migrationBuilder.DropColumn(
                name: "IsTemporary",
                table: "ResidentAccessCredentials");

            migrationBuilder.DropColumn(
                name: "MaxRenewals",
                table: "ResidentAccessCredentials");

            migrationBuilder.DropColumn(
                name: "RenewalCount",
                table: "ResidentAccessCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_CPF",
                table: "UserAccess",
                column: "CPF",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_PhoneNumber",
                table: "UserAccess",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccess_RG",
                table: "UserAccess",
                column: "RG",
                unique: true);
        }
    }
}

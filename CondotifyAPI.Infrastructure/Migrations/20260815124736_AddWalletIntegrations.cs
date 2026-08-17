using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnterpriseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    AuthenticationMode = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsValidated = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IssuerId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ServiceAccountEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ClassSuffix = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PassTypeIdentifier = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    TeamIdentifier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CredentialSecret = table.Column<string>(type: "text", nullable: false),
                    CredentialPassword = table.Column<string>(type: "text", nullable: false),
                    IntermediateCertificate = table.Column<string>(type: "text", nullable: false),
                    CredentialFingerprint = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CredentialExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidationMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletIntegrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletIntegrations_EnterpriseId_IsActive",
                table: "WalletIntegrations",
                columns: new[] { "EnterpriseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletIntegrations_EnterpriseId_Provider",
                table: "WalletIntegrations",
                columns: new[] { "EnterpriseId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletIntegrations");
        }
    }
}

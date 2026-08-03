using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentPasswordRecoveryTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResidentPasswordRecoveryTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentPasswordRecoveryTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPasswordRecoveryTokens_ResidentId_UsedAt",
                table: "ResidentPasswordRecoveryTokens",
                columns: new[] { "ResidentId", "UsedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResidentPasswordRecoveryTokens_TokenHash",
                table: "ResidentPasswordRecoveryTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResidentPasswordRecoveryTokens");
        }
    }
}

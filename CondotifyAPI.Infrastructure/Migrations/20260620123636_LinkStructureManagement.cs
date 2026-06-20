using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkStructureManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Units_BlockId",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_Resident_UnitId",
                table: "Resident");

            migrationBuilder.DropIndex(
                name: "IX_Blocks_LicenseId",
                table: "Blocks");

            migrationBuilder.CreateIndex(
                name: "IX_Units_BlockId_Number",
                table: "Units",
                columns: new[] { "BlockId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident",
                columns: new[] { "UnitId", "CPF" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_LicenseId_Name",
                table: "Blocks",
                columns: new[] { "LicenseId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Units_BlockId_Number",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident");

            migrationBuilder.DropIndex(
                name: "IX_Blocks_LicenseId_Name",
                table: "Blocks");

            migrationBuilder.CreateIndex(
                name: "IX_Units_BlockId",
                table: "Units",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Resident_UnitId",
                table: "Resident",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_LicenseId",
                table: "Blocks",
                column: "LicenseId");
        }
    }
}

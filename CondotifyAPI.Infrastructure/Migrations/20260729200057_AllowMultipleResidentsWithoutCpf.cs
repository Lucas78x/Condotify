using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleResidentsWithoutCpf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident");

            migrationBuilder.CreateIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident",
                columns: new[] { "UnitId", "CPF" },
                unique: true,
                filter: "\"CPF\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident");

            migrationBuilder.CreateIndex(
                name: "IX_Resident_UnitId_CPF",
                table: "Resident",
                columns: new[] { "UnitId", "CPF" },
                unique: true);
        }
    }
}

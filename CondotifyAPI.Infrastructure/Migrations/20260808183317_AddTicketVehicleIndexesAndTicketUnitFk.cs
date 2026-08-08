using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketVehicleIndexesAndTicketUnitFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_LicenseId",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LicenseId_Status_CreatedDate",
                table: "Tickets",
                columns: new[] { "LicenseId", "Status", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UnitId",
                table: "Tickets",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Units_UnitId",
                table: "Tickets",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Units_UnitId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_LicenseId_Status_CreatedDate",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_UnitId",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LicenseId",
                table: "Tickets",
                column: "LicenseId");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deliveries_LicenseId",
                table: "Deliveries");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_LicenseId_Status_CreatedAt",
                table: "Deliveries",
                columns: new[] { "LicenseId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_LicenseId_TrackingCode",
                table: "Deliveries",
                columns: new[] { "LicenseId", "TrackingCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deliveries_LicenseId_Status_CreatedAt",
                table: "Deliveries");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_LicenseId_TrackingCode",
                table: "Deliveries");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_LicenseId",
                table: "Deliveries",
                column: "LicenseId");
        }
    }
}

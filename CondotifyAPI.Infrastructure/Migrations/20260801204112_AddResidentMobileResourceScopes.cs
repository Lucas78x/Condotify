using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResidentMobileResourceScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecipientResidentId",
                table: "Deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                table: "Deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResidentVisible",
                table: "CFTVDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_RecipientResidentId_Status_CreatedAt",
                table: "Deliveries",
                columns: new[] { "RecipientResidentId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_UnitId",
                table: "Deliveries",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Deliveries_Resident_RecipientResidentId",
                table: "Deliveries",
                column: "RecipientResidentId",
                principalTable: "Resident",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Deliveries_Units_UnitId",
                table: "Deliveries",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deliveries_Resident_RecipientResidentId",
                table: "Deliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_Deliveries_Units_UnitId",
                table: "Deliveries");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_RecipientResidentId_Status_CreatedAt",
                table: "Deliveries");

            migrationBuilder.DropIndex(
                name: "IX_Deliveries_UnitId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "RecipientResidentId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "ResidentVisible",
                table: "CFTVDevices");
        }
    }
}

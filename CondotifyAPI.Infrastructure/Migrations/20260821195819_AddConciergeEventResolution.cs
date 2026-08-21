using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConciergeEventResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttentionResolutionNote",
                table: "AccessEventRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AttentionResolvedAt",
                table: "AccessEventRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttentionResolvedBy",
                table: "AccessEventRecords",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AccessEventRecords_LicenseId_Authorized_AttentionResolvedAt",
                table: "AccessEventRecords",
                columns: new[] { "LicenseId", "Authorized", "AttentionResolvedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessEventRecords_LicenseId_Authorized_AttentionResolvedAt",
                table: "AccessEventRecords");

            migrationBuilder.DropColumn(
                name: "AttentionResolutionNote",
                table: "AccessEventRecords");

            migrationBuilder.DropColumn(
                name: "AttentionResolvedAt",
                table: "AccessEventRecords");

            migrationBuilder.DropColumn(
                name: "AttentionResolvedBy",
                table: "AccessEventRecords");
        }
    }
}

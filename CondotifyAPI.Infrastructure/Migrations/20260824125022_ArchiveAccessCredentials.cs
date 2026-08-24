using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveAccessCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ResidentAccessCredentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentAccessCredentials_ArchivedAt",
                table: "ResidentAccessCredentials",
                column: "ArchivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResidentAccessCredentials_ArchivedAt",
                table: "ResidentAccessCredentials");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ResidentAccessCredentials");
        }
    }
}

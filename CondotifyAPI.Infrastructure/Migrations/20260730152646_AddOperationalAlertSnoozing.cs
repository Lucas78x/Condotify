using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAlertSnoozing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuppressedBy",
                table: "OperationalAlerts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SuppressedById",
                table: "OperationalAlerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuppressedUntil",
                table: "OperationalAlerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuppressionReason",
                table: "OperationalAlerts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuppressedBy",
                table: "OperationalAlerts");

            migrationBuilder.DropColumn(
                name: "SuppressedById",
                table: "OperationalAlerts");

            migrationBuilder.DropColumn(
                name: "SuppressedUntil",
                table: "OperationalAlerts");

            migrationBuilder.DropColumn(
                name: "SuppressionReason",
                table: "OperationalAlerts");
        }
    }
}

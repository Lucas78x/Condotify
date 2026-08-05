using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLprSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LprCameraChannel",
                table: "AccessControlDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LprCameraId",
                table: "AccessControlDevices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LprMode",
                table: "AccessControlDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VehicleAccessAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessControlDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlateRead = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    MatchedVehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    SnapshotReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleAccessAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleAccessAudits_AccessControlDevices_AccessControlDevic~",
                        column: x => x.AccessControlDeviceId,
                        principalTable: "AccessControlDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessControlDevices_LprCameraId",
                table: "AccessControlDevices",
                column: "LprCameraId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAccessAudits_AccessControlDeviceId_Timestamp",
                table: "VehicleAccessAudits",
                columns: new[] { "AccessControlDeviceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleAccessAudits_MatchedVehicleId",
                table: "VehicleAccessAudits",
                column: "MatchedVehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessControlDevices_CFTVDevices_LprCameraId",
                table: "AccessControlDevices",
                column: "LprCameraId",
                principalTable: "CFTVDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AccessControlDevices_CFTVDevices_LprCameraId",
                table: "AccessControlDevices");

            migrationBuilder.DropTable(
                name: "VehicleAccessAudits");

            migrationBuilder.DropIndex(
                name: "IX_AccessControlDevices_LprCameraId",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LprCameraChannel",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LprCameraId",
                table: "AccessControlDevices");

            migrationBuilder.DropColumn(
                name: "LprMode",
                table: "AccessControlDevices");
        }
    }
}

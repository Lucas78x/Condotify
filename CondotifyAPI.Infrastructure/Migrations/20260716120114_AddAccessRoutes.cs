using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Audience = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AllowTemporary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DaysOfWeekMask = table.Column<int>(type: "integer", nullable: false, defaultValue: 127),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessRoutes_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessRouteDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessRouteId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortalNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRouteDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessRouteDevices_AccessControlDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "AccessControlDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AccessRouteDevices_AccessRoutes_AccessRouteId",
                        column: x => x.AccessRouteId,
                        principalTable: "AccessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRouteDevices_AccessRouteId_DeviceId_PortalNumber",
                table: "AccessRouteDevices",
                columns: new[] { "AccessRouteId", "DeviceId", "PortalNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessRouteDevices_DeviceId",
                table: "AccessRouteDevices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoutes_LicenseId_Name",
                table: "AccessRoutes",
                columns: new[] { "LicenseId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessRouteDevices");

            migrationBuilder.DropTable(
                name: "AccessRoutes");
        }
    }
}

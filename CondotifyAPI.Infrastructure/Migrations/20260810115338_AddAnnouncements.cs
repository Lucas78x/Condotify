using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "EnabledModules",
                table: "Licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 2047L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 1023L);

            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Announcements_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_LicenseId_CreatedAt",
                table: "Announcements",
                columns: new[] { "LicenseId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.AlterColumn<long>(
                name: "EnabledModules",
                table: "Licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 1023L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 2047L);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedRecycleBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecycleBinItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    DeletedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RestoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RestoredBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecycleBinItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecycleBinItems_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleBinItems_LicenseId_EntityType_EntityId",
                table: "RecycleBinItems",
                columns: new[] { "LicenseId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecycleBinItems_LicenseId_RestoredAt_ExpiresAt",
                table: "RecycleBinItems",
                columns: new[] { "LicenseId", "RestoredAt", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecycleBinItems");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvolvePropertyPeopleAndVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommercialPhone",
                table: "Resident",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Resident",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Resident",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyAccess",
                table: "Resident",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelPlural",
                table: "Licenses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Blocos");

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelSingular",
                table: "Licenses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Bloco");

            migrationBuilder.AddColumn<string>(
                name: "UnitLabelPlural",
                table: "Licenses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unidades");

            migrationBuilder.AddColumn<string>(
                name: "UnitLabelSingular",
                table: "Licenses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unidade");

            migrationBuilder.CreateTable(
                name: "RegistrationInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Contact = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrationInvites_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegistrationInvites_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResidentUnitLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relationship = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResidentUnitLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResidentUnitLinks_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResidentUnitLinks_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Plate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Brand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TagIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Vehicles_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "ResidentUnitLinks"
                    ("Id", "ResidentId", "UnitId", "Relationship", "Description", "IsPrimary", "IsActive", "StartsAt", "CreatedAt", "UpdatedAt")
                SELECT
                    r."Id",
                    r."Id",
                    r."UnitId",
                    CASE r."AccessType" WHEN 1 THEN 3 WHEN 2 THEN 4 ELSE 0 END,
                    r."Description",
                    TRUE,
                    TRUE,
                    r."CreatedAt",
                    r."CreatedAt",
                    r."CreatedAt"
                FROM "Resident" r;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationInvites_LicenseId_Status_SentAt",
                table: "RegistrationInvites",
                columns: new[] { "LicenseId", "Status", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationInvites_ResidentId",
                table: "RegistrationInvites",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationInvites_TokenHash",
                table: "RegistrationInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentUnitLinks_ResidentId_UnitId",
                table: "ResidentUnitLinks",
                columns: new[] { "ResidentId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResidentUnitLinks_UnitId_IsActive",
                table: "ResidentUnitLinks",
                columns: new[] { "UnitId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_ResidentId",
                table: "Vehicles",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_TagIdentifier",
                table: "Vehicles",
                column: "TagIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_UnitId_Plate",
                table: "Vehicles",
                columns: new[] { "UnitId", "Plate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationInvites");

            migrationBuilder.DropTable(
                name: "ResidentUnitLinks");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CommercialPhone",
                table: "Resident");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Resident");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Resident");

            migrationBuilder.DropColumn(
                name: "NotifyAccess",
                table: "Resident");

            migrationBuilder.DropColumn(
                name: "GroupLabelPlural",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "GroupLabelSingular",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "UnitLabelPlural",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "UnitLabelSingular",
                table: "Licenses");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAmenityBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Amenities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FeeAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    FeeDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RequiresTermsAcceptance = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TermsText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    MonthlyLimitPerUnit = table.Column<int>(type: "integer", nullable: true),
                    MinAdvanceNoticeHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxAdvanceDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    CancellationCutoffHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Amenities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Amenities_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AmenityBlackouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmenityBlackouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmenityBlackouts_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AmenityScheduleSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmenityScheduleSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmenityScheduleSlots_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AmenityBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AmenityId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmenityBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmenityBookings_Amenities_AmenityId",
                        column: x => x.AmenityId,
                        principalTable: "Amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AmenityBookings_AmenityScheduleSlots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "AmenityScheduleSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmenityBookings_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AmenityBookings_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AmenityBookings_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Amenities_LicenseId_Name",
                table: "Amenities",
                columns: new[] { "LicenseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBlackouts_AmenityId_StartDate_EndDate",
                table: "AmenityBlackouts",
                columns: new[] { "AmenityId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBookings_AmenityId_SlotId_Date",
                table: "AmenityBookings",
                columns: new[] { "AmenityId", "SlotId", "Date" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBookings_LicenseId_UnitId_Date",
                table: "AmenityBookings",
                columns: new[] { "LicenseId", "UnitId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBookings_ResidentId",
                table: "AmenityBookings",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBookings_SlotId",
                table: "AmenityBookings",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_AmenityBookings_UnitId",
                table: "AmenityBookings",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AmenityScheduleSlots_AmenityId_DayOfWeek",
                table: "AmenityScheduleSlots",
                columns: new[] { "AmenityId", "DayOfWeek" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AmenityBlackouts");

            migrationBuilder.DropTable(
                name: "AmenityBookings");

            migrationBuilder.DropTable(
                name: "AmenityScheduleSlots");

            migrationBuilder.DropTable(
                name: "Amenities");
        }
    }
}

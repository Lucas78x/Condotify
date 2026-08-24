using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernanceAssemblies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "EnabledModules",
                table: "Licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 4095L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 2047L);

            // O novo módulo é habilitado nas licenças existentes. Administradores e
            // gestores recebem as novas permissões; os demais papéis continuam sem acesso.
            migrationBuilder.Sql("UPDATE \"Licenses\" SET \"EnabledModules\" = \"EnabledModules\" | 2048;");
            migrationBuilder.Sql("UPDATE \"LicenseUserAccesses\" SET \"Permissions\" = \"Permissions\" | 206158430208 WHERE \"Role\" IN (0, 1);");

            migrationBuilder.CreateTable(
                name: "CondominiumAssemblies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VoteVisibility = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    MeetingUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VotingStartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VotingEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AllowVoteChange = table.Column<bool>(type: "boolean", nullable: false),
                    ShowResultsBeforeClose = table.Column<bool>(type: "boolean", nullable: false),
                    RequireResponsibleResident = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CondominiumAssemblies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CondominiumAssemblies_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyAgendaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    QuorumPercentage = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    ApprovalPercentage = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    AbstentionCountsForQuorum = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyAgendaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyAgendaItems_CondominiumAssemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "CondominiumAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyAttendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyAttendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyAttendances_CondominiumAssemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "CondominiumAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssemblyAttendances_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyAttendances_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyAudits_CondominiumAssemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "CondominiumAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyEligibleUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    IsEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IneligibilityReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyEligibleUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyEligibleUnits_CondominiumAssemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "CondominiumAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssemblyEligibleUnits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyVoteOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgendaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    IsApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IsAbstention = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyVoteOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyVoteOptions_AssemblyAgendaItems_AgendaItemId",
                        column: x => x.AgendaItemId,
                        principalTable: "AssemblyAgendaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgendaItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyVotes_AssemblyAgendaItems_AgendaItemId",
                        column: x => x.AgendaItemId,
                        principalTable: "AssemblyAgendaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssemblyVotes_AssemblyVoteOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "AssemblyVoteOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyVotes_CondominiumAssemblies_AssemblyId",
                        column: x => x.AssemblyId,
                        principalTable: "CondominiumAssemblies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssemblyVotes_Resident_ResidentId",
                        column: x => x.ResidentId,
                        principalTable: "Resident",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyVotes_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyAgendaItems_AssemblyId_Order",
                table: "AssemblyAgendaItems",
                columns: new[] { "AssemblyId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyAttendances_AssemblyId_UnitId",
                table: "AssemblyAttendances",
                columns: new[] { "AssemblyId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyAttendances_ResidentId",
                table: "AssemblyAttendances",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyAttendances_UnitId",
                table: "AssemblyAttendances",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyAudits_AssemblyId_CreatedAt",
                table: "AssemblyAudits",
                columns: new[] { "AssemblyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyEligibleUnits_AssemblyId_UnitId",
                table: "AssemblyEligibleUnits",
                columns: new[] { "AssemblyId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyEligibleUnits_UnitId",
                table: "AssemblyEligibleUnits",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVoteOptions_AgendaItemId_Order",
                table: "AssemblyVoteOptions",
                columns: new[] { "AgendaItemId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVotes_AgendaItemId_UnitId",
                table: "AssemblyVotes",
                columns: new[] { "AgendaItemId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVotes_AssemblyId_CastAt",
                table: "AssemblyVotes",
                columns: new[] { "AssemblyId", "CastAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVotes_OptionId",
                table: "AssemblyVotes",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVotes_ResidentId",
                table: "AssemblyVotes",
                column: "ResidentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyVotes_UnitId",
                table: "AssemblyVotes",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CondominiumAssemblies_LicenseId_Status_StartsAt",
                table: "CondominiumAssemblies",
                columns: new[] { "LicenseId", "Status", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"LicenseUserAccesses\" SET \"Permissions\" = \"Permissions\" & ~206158430208;");
            migrationBuilder.Sql("UPDATE \"Licenses\" SET \"EnabledModules\" = \"EnabledModules\" & ~2048;");

            migrationBuilder.DropTable(
                name: "AssemblyAttendances");

            migrationBuilder.DropTable(
                name: "AssemblyAudits");

            migrationBuilder.DropTable(
                name: "AssemblyEligibleUnits");

            migrationBuilder.DropTable(
                name: "AssemblyVotes");

            migrationBuilder.DropTable(
                name: "AssemblyVoteOptions");

            migrationBuilder.DropTable(
                name: "AssemblyAgendaItems");

            migrationBuilder.DropTable(
                name: "CondominiumAssemblies");

            migrationBuilder.AlterColumn<long>(
                name: "EnabledModules",
                table: "Licenses",
                type: "bigint",
                nullable: false,
                defaultValue: 2047L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValue: 4095L);
        }
    }
}

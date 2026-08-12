using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VisibleToResident",
                table: "IncidentTimelineEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocationLabel",
                table: "Incidents",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ReportedByResidentId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaResolutionDueAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaResponseDueAt",
                table: "Incidents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaintenancePolicies",
                columns: table => new
                {
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LowResponseMinutes = table.Column<int>(type: "integer", nullable: false),
                    LowResolutionMinutes = table.Column<int>(type: "integer", nullable: false),
                    MediumResponseMinutes = table.Column<int>(type: "integer", nullable: false),
                    MediumResolutionMinutes = table.Column<int>(type: "integer", nullable: false),
                    HighResponseMinutes = table.Column<int>(type: "integer", nullable: false),
                    HighResolutionMinutes = table.Column<int>(type: "integer", nullable: false),
                    CriticalResponseMinutes = table.Column<int>(type: "integer", nullable: false),
                    CriticalResolutionMinutes = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePolicies", x => x.LicenseId);
                    table.ForeignKey(
                        name: "FK_MaintenancePolicies_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Specialty = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceProviders_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreventiveMaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    LocationLabel = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    LeadDays = table.Column<int>(type: "integer", nullable: false),
                    NextDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastGeneratedFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DefaultProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultAssignedToName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ChecklistTemplateJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreventiveMaintenancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenancePlans_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PreventiveMaintenancePlans_MaintenanceProviders_DefaultProv~",
                        column: x => x.DefaultProviderId,
                        principalTable: "MaintenanceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreventivePlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    LocationLabel = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ActualCost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CompletionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrders_MaintenanceProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "MaintenanceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkOrders_PreventiveMaintenancePlans_PreventivePlanId",
                        column: x => x.PreventivePlanId,
                        principalTable: "PreventiveMaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IncidentAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MediaReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VisibleToResident = table.Column<bool>(type: "boolean", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByResidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAttachments", x => x.Id);
                    table.CheckConstraint("CK_IncidentAttachments_Target", "(\"IncidentId\" IS NOT NULL) OR (\"WorkOrderId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_IncidentAttachments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentAttachments_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    VisibleToResident = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderActivities_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderChecklistItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_IncidentId",
                table: "IncidentAttachments",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_LicenseId_CreatedAt",
                table: "IncidentAttachments",
                columns: new[] { "LicenseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_WorkOrderId",
                table: "IncidentAttachments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProviders_LicenseId_IsActive",
                table: "MaintenanceProviders",
                columns: new[] { "LicenseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceProviders_LicenseId_Name",
                table: "MaintenanceProviders",
                columns: new[] { "LicenseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenancePlans_DefaultProviderId",
                table: "PreventiveMaintenancePlans",
                column: "DefaultProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenancePlans_LicenseId_IsActive_NextDueAt",
                table: "PreventiveMaintenancePlans",
                columns: new[] { "LicenseId", "IsActive", "NextDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PreventiveMaintenancePlans_LicenseId_Name",
                table: "PreventiveMaintenancePlans",
                columns: new[] { "LicenseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderActivities_WorkOrderId_CreatedAt",
                table: "WorkOrderActivities",
                columns: new[] { "WorkOrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChecklistItems_WorkOrderId_SortOrder",
                table: "WorkOrderChecklistItems",
                columns: new[] { "WorkOrderId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_IncidentId",
                table: "WorkOrders",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_LicenseId_Code",
                table: "WorkOrders",
                columns: new[] { "LicenseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_LicenseId_Status_DueAt",
                table: "WorkOrders",
                columns: new[] { "LicenseId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_PreventivePlanId_ScheduledFor",
                table: "WorkOrders",
                columns: new[] { "PreventivePlanId", "ScheduledFor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProviderId",
                table: "WorkOrders",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentAttachments");

            migrationBuilder.DropTable(
                name: "MaintenancePolicies");

            migrationBuilder.DropTable(
                name: "WorkOrderActivities");

            migrationBuilder.DropTable(
                name: "WorkOrderChecklistItems");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "PreventiveMaintenancePlans");

            migrationBuilder.DropTable(
                name: "MaintenanceProviders");

            migrationBuilder.DropColumn(
                name: "VisibleToResident",
                table: "IncidentTimelineEntries");

            migrationBuilder.DropColumn(
                name: "LocationLabel",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "ReportedByResidentId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "SlaResolutionDueAt",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "SlaResponseDueAt",
                table: "Incidents");
        }
    }
}

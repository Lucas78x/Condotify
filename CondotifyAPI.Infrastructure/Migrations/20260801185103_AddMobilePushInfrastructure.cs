using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMobilePushInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PushToken = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Locale = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushInstallations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Route = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DeepLink = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    DeduplicationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FanoutCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResponseCode = table.Column<int>(type: "integer", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushInstallations_InstallationId",
                        column: x => x.InstallationId,
                        principalTable: "PushInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushNotifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "PushNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_InstallationId",
                table: "PushDeliveries",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_NotificationId_InstallationId",
                table: "PushDeliveries",
                columns: new[] { "NotificationId", "InstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_Status_NextAttemptAt_CreatedAt",
                table: "PushDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_InstallationId",
                table: "PushInstallations",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_SubjectType_SubjectId_IsActive",
                table: "PushInstallations",
                columns: new[] { "SubjectType", "SubjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_TokenHash",
                table: "PushInstallations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushNotifications_SubjectType_SubjectId_CreatedAt",
                table: "PushNotifications",
                columns: new[] { "SubjectType", "SubjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PushNotifications_SubjectType_SubjectId_DeduplicationKey",
                table: "PushNotifications",
                columns: new[] { "SubjectType", "SubjectId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushPreferences_SubjectType_SubjectId_Category",
                table: "PushPreferences",
                columns: new[] { "SubjectType", "SubjectId", "Category" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushDeliveries");

            migrationBuilder.DropTable(
                name: "PushPreferences");

            migrationBuilder.DropTable(
                name: "PushInstallations");

            migrationBuilder.DropTable(
                name: "PushNotifications");
        }
    }
}

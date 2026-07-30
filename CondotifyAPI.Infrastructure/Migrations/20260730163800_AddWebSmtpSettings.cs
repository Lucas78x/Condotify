using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebSmtpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnableSsl",
                table: "AlertNotificationPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromEmail",
                table: "AlertNotificationPolicies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpFromName",
                table: "AlertNotificationPolicies",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "AlertNotificationPolicies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "AlertNotificationPolicies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "AlertNotificationPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 587);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "AlertNotificationPolicies",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmtpEnableSsl",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpFromEmail",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpFromName",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "AlertNotificationPolicies");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "AlertNotificationPolicies");
        }
    }
}

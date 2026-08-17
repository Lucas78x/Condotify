using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseUrlKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlKey",
                table: "Licenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                WITH normalized AS (
                    SELECT
                        "Id",
                        COALESCE(
                            NULLIF(LOWER(TRIM(BOTH '-' FROM REGEXP_REPLACE(COALESCE(NULLIF("Code", ''), "Name"), '[^A-Za-z0-9]+', '-', 'g'))), ''),
                            'condominio') AS base_key
                    FROM "Licenses"
                ), ranked AS (
                    SELECT
                        "Id",
                        base_key,
                        ROW_NUMBER() OVER (PARTITION BY base_key ORDER BY "Id") AS key_rank
                    FROM normalized
                )
                UPDATE "Licenses" AS license
                SET "UrlKey" = CASE
                    WHEN ranked.key_rank = 1 THEN LEFT(ranked.base_key, 100)
                    ELSE LEFT(ranked.base_key, 91) || '-' || LEFT(REPLACE(license."Id"::text, '-', ''), 8)
                END
                FROM ranked
                WHERE ranked."Id" = license."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_UrlKey",
                table: "Licenses",
                column: "UrlKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Licenses_UrlKey",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "UrlKey",
                table: "Licenses");
        }
    }
}

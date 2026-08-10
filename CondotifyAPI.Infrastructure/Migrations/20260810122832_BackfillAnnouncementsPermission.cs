using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAnnouncementsPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: LicenseUserAccesses.Permissions e um bitmask PERSISTIDO, gravado
            // uma unica vez na criacao/edicao do acesso - nunca re-derivado do Role. Sem
            // isto, todo sindico (Administrator = 0) e gerente (Manager = 1) ja existente
            // ficaria sem o bit novo e o modulo Comunicados simplesmente nao apareceria
            // para ele. Concierge/Operator/Viewer nao recebem acesso a comunicados.
            // ManageAnnouncements = 1<<35 (LicensePermissionEnum) = 34359738368.
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 34359738368
                WHERE "Role" IN (0, 1);
                """);

            // Backfill: a migration AddAnnouncements apenas alterou o DEFAULT da coluna
            // EnabledModules (para 2047); no Postgres isso NAO reescreve as linhas ja
            // existentes, entao toda licenca anterior continuaria com o bit do modulo
            // Comunicados desligado. Announcements = 1<<10 (LicenseModuleEnum) = 1024.
            migrationBuilder.Sql("""
                UPDATE "Licenses"
                SET "EnabledModules" = "EnabledModules" | 1024;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" & ~34359738368
                WHERE "Role" IN (0, 1);
                """);

            migrationBuilder.Sql("""
                UPDATE "Licenses"
                SET "EnabledModules" = "EnabledModules" & ~1024;
                """);
        }
    }
}

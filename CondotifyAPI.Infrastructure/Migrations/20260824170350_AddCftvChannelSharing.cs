using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCftvChannelSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ResidentVisible",
                table: "CFTVChannels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserva exatamente o acesso que já existia: quando um equipamento
            // estava compartilhado, todos os seus canais ativos permanecem visíveis.
            migrationBuilder.Sql(
                """
                UPDATE "CFTVChannels" AS channel
                SET "ResidentVisible" = device."ResidentVisible"
                FROM "CFTVDevices" AS device
                WHERE channel."CFTVDeviceId" = device."Id"
                  AND channel."IsEnabled" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResidentVisible",
                table: "CFTVChannels");
        }
    }
}

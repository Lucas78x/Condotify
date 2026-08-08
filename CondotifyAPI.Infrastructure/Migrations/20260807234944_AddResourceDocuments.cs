using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    StorageReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceDocuments_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceDocuments_LicenseId_Category_PublishedAt",
                table: "ResourceDocuments",
                columns: new[] { "LicenseId", "Category", "PublishedAt" });

            // Backfill: LicenseUserAccesses.Permissions e um bitmask PERSISTIDO, gravado
            // uma unica vez na criacao/edicao do acesso - nunca re-derivado do Role. Sem
            // isto, todo sindico (Administrator = 0) e gerente (Manager = 1) ja existente
            // ficaria sem os bits novos e o modulo Documentos simplesmente nao apareceria
            // para ele. Concierge/Operator/Viewer nao recebem acesso a documentos.
            // ViewDocuments = 1<<33 = 8589934592, ManageDocuments = 1<<34 = 17179869184
            // (LicensePermissionEnum), somados = 25769803776.
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 25769803776
                WHERE "Role" IN (0, 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" & ~25769803776
                WHERE "Role" IN (0, 1);
                """);

            migrationBuilder.DropTable(
                name: "ResourceDocuments");
        }
    }
}

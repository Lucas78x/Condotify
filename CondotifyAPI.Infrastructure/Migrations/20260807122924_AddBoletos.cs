using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CondotifyAPI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBoletos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoletoBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    TotalPages = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoletoBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoletoBatches_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoletoDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    MatchMethod = table.Column<int>(type: "integer", nullable: false),
                    Ignored = table.Column<bool>(type: "boolean", nullable: false),
                    StorageReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExtractedSnippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoletoDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoletoDocuments_BoletoBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BoletoBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BoletoDocuments_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoletoBatches_LicenseId_Status_CreatedAt",
                table: "BoletoBatches",
                columns: new[] { "LicenseId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BoletoDocuments_BatchId",
                table: "BoletoDocuments",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BoletoDocuments_UnitId",
                table: "BoletoDocuments",
                column: "UnitId");

            // Backfill: LicenseUserAccesses.Permissions e um bitmask PERSISTIDO, gravado
            // uma unica vez na criacao/edicao do acesso - nunca re-derivado do Role. Sem
            // isto, todo sindico (Administrator = 0) e gerente (Manager = 1) ja existente
            // ficaria sem os bits novos e o modulo Boletos simplesmente nao apareceria
            // para ele. Concierge/Operator/Viewer nao recebem acesso financeiro.
            // ViewFinance = 1<<31 = 2147483648, ManageFinance = 1<<32 = 4294967296
            // (LicensePermissionEnum), somados = 6442450944.
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" | 6442450944
                WHERE "Role" IN (0, 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Desfaz o backfill: sem as tabelas de boleto os bits financeiros nao
            // significam nada, e nenhuma outra funcionalidade usa esses dois bits.
            migrationBuilder.Sql("""
                UPDATE "LicenseUserAccesses"
                SET "Permissions" = "Permissions" & ~6442450944;
                """);

            migrationBuilder.DropTable(
                name: "BoletoDocuments");

            migrationBuilder.DropTable(
                name: "BoletoBatches");
        }
    }
}

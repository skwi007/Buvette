using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buvette.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreventesHelloAsso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TarifHelloAssoId",
                table: "Produits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HelloAssoCampagne",
                table: "Evenements",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HelloAssoSynchroLe",
                table: "Evenements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HelloAssoType",
                table: "Evenements",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PreCommandes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EvenementId = table.Column<int>(type: "INTEGER", nullable: false),
                    NumeroHelloAsso = table.Column<long>(type: "INTEGER", nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Prenom = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DateCommande = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MontantPaye = table.Column<decimal>(type: "TEXT", nullable: false),
                    Annulee = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetireeLe = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreCommandes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreCommandes_Evenements_EvenementId",
                        column: x => x.EvenementId,
                        principalTable: "Evenements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LignesPreCommandes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PreCommandeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProduitId = table.Column<int>(type: "INTEGER", nullable: true),
                    NomArticle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantite = table.Column<int>(type: "INTEGER", nullable: false),
                    Montant = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LignesPreCommandes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LignesPreCommandes_PreCommandes_PreCommandeId",
                        column: x => x.PreCommandeId,
                        principalTable: "PreCommandes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LignesPreCommandes_Produits_ProduitId",
                        column: x => x.ProduitId,
                        principalTable: "Produits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LignesPreCommandes_PreCommandeId",
                table: "LignesPreCommandes",
                column: "PreCommandeId");

            migrationBuilder.CreateIndex(
                name: "IX_LignesPreCommandes_ProduitId",
                table: "LignesPreCommandes",
                column: "ProduitId");

            migrationBuilder.CreateIndex(
                name: "IX_PreCommandes_EvenementId_NumeroHelloAsso",
                table: "PreCommandes",
                columns: new[] { "EvenementId", "NumeroHelloAsso" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LignesPreCommandes");

            migrationBuilder.DropTable(
                name: "PreCommandes");

            migrationBuilder.DropColumn(
                name: "TarifHelloAssoId",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "HelloAssoCampagne",
                table: "Evenements");

            migrationBuilder.DropColumn(
                name: "HelloAssoSynchroLe",
                table: "Evenements");

            migrationBuilder.DropColumn(
                name: "HelloAssoType",
                table: "Evenements");
        }
    }
}

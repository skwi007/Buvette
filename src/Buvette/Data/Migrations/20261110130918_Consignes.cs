using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buvette.Data.Migrations
{
    /// <inheritdoc />
    public partial class Consignes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Consigne",
                table: "Produits",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsigneUnitaire",
                table: "Lignes",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Consigne",
                table: "Evenements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Consignes",
                table: "Commandes",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsignesRendues",
                table: "Commandes",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RetoursConsignes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandeId = table.Column<int>(type: "INTEGER", nullable: false),
                    MontantUnitaire = table.Column<decimal>(type: "TEXT", nullable: false),
                    Quantite = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetoursConsignes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetoursConsignes_Commandes_CommandeId",
                        column: x => x.CommandeId,
                        principalTable: "Commandes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetoursConsignes_CommandeId",
                table: "RetoursConsignes",
                column: "CommandeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetoursConsignes");

            migrationBuilder.DropColumn(
                name: "Consigne",
                table: "Produits");

            migrationBuilder.DropColumn(
                name: "ConsigneUnitaire",
                table: "Lignes");

            migrationBuilder.DropColumn(
                name: "Consigne",
                table: "Evenements");

            migrationBuilder.DropColumn(
                name: "Consignes",
                table: "Commandes");

            migrationBuilder.DropColumn(
                name: "ConsignesRendues",
                table: "Commandes");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buvette.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComptageDeCaisseEtGratuites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontantCompte",
                table: "Evenements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Offerte",
                table: "Commandes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MontantCompte",
                table: "Evenements");

            migrationBuilder.DropColumn(
                name: "Offerte",
                table: "Commandes");
        }
    }
}

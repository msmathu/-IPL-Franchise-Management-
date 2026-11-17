using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CricsheetImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CricsheetId",
                schema: "ipl",
                table: "Players",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                schema: "ipl",
                table: "Matches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_CricsheetId",
                schema: "ipl",
                table: "Players",
                column: "CricsheetId",
                unique: true,
                filter: "[CricsheetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ExternalRef",
                schema: "ipl",
                table: "Matches",
                column: "ExternalRef",
                unique: true,
                filter: "[ExternalRef] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_CricsheetId",
                schema: "ipl",
                table: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Matches_ExternalRef",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "CricsheetId",
                schema: "ipl",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                schema: "ipl",
                table: "Matches");
        }
    }
}

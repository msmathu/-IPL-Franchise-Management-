using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerPhotoCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoCredit",
                schema: "ipl",
                table: "Players",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoSourceUrl",
                schema: "ipl",
                table: "Players",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoCredit",
                schema: "ipl",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PhotoSourceUrl",
                schema: "ipl",
                table: "Players");
        }
    }
}

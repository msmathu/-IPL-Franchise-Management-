using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DataProtectionKeyXml : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Xml",
                schema: "ipl",
                table: "DataProtectionKeys",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Xml",
                schema: "ipl",
                table: "DataProtectionKeys",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}

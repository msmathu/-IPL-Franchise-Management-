using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "ipl",
                table: "Matches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchNumber",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoResult",
                schema: "ipl",
                table: "Matches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OversLimit",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Stage",
                schema: "ipl",
                table: "Matches",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team1Balls",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Team1BattedFirst",
                schema: "ipl",
                table: "Matches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Team1Runs",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team1Wickets",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2Balls",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2OversLimit",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2Runs",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2Wickets",
                schema: "ipl",
                table: "Matches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Winner",
                schema: "ipl",
                table: "Matches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "City",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "MatchNumber",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "NoResult",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "OversLimit",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Stage",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team1Balls",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team1BattedFirst",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team1Runs",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team1Wickets",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team2Balls",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team2OversLimit",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team2Runs",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Team2Wickets",
                schema: "ipl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "Winner",
                schema: "ipl",
                table: "Matches");
        }
    }
}

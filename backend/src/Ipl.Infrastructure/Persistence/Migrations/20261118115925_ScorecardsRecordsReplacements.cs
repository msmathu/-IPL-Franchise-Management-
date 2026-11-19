using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ipl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScorecardsRecordsReplacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScorecardJson",
                schema: "ipl",
                table: "Matches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BatterBowlerMatchups",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    BatterId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BatterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BowlerId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    BowlerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Balls = table.Column<int>(type: "int", nullable: false),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    Dismissals = table.Column<int>(type: "int", nullable: false),
                    Fours = table.Column<int>(type: "int", nullable: false),
                    Sixes = table.Column<int>(type: "int", nullable: false),
                    Dots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatterBowlerMatchups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatterBowlerMatchups_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "ipl",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonSeasonStats",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Team = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false),
                    Innings = table.Column<int>(type: "int", nullable: false),
                    NotOuts = table.Column<int>(type: "int", nullable: false),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    Balls = table.Column<int>(type: "int", nullable: false),
                    HighestScore = table.Column<int>(type: "int", nullable: false),
                    HighestNotOut = table.Column<bool>(type: "bit", nullable: false),
                    Fifties = table.Column<int>(type: "int", nullable: false),
                    Hundreds = table.Column<int>(type: "int", nullable: false),
                    Fours = table.Column<int>(type: "int", nullable: false),
                    Sixes = table.Column<int>(type: "int", nullable: false),
                    BallsBowled = table.Column<int>(type: "int", nullable: false),
                    RunsConceded = table.Column<int>(type: "int", nullable: false),
                    Wickets = table.Column<int>(type: "int", nullable: false),
                    DotBalls = table.Column<int>(type: "int", nullable: false),
                    BestWickets = table.Column<int>(type: "int", nullable: false),
                    BestRuns = table.Column<int>(type: "int", nullable: false),
                    Catches = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonSeasonStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonSeasonStats_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "ipl",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReplacementRequests",
                schema: "ipl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    InjuredPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReplacementPlayerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementRequests_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalSchema: "ipl",
                        principalTable: "Franchises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplacementRequests_Players_InjuredPlayerId",
                        column: x => x.InjuredPlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplacementRequests_Players_ReplacementPlayerId",
                        column: x => x.ReplacementPlayerId,
                        principalSchema: "ipl",
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatterBowlerMatchups_CompetitionId_BatterId_BowlerId",
                schema: "ipl",
                table: "BatterBowlerMatchups",
                columns: new[] { "CompetitionId", "BatterId", "BowlerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BatterBowlerMatchups_CompetitionId_BowlerId",
                schema: "ipl",
                table: "BatterBowlerMatchups",
                columns: new[] { "CompetitionId", "BowlerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonSeasonStats_CompetitionId_PersonId",
                schema: "ipl",
                table: "PersonSeasonStats",
                columns: new[] { "CompetitionId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonSeasonStats_CompetitionId_Year",
                schema: "ipl",
                table: "PersonSeasonStats",
                columns: new[] { "CompetitionId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_FranchiseId_SeasonId_Status",
                schema: "ipl",
                table: "ReplacementRequests",
                columns: new[] { "FranchiseId", "SeasonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_InjuredPlayerId",
                schema: "ipl",
                table: "ReplacementRequests",
                column: "InjuredPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_ReplacementPlayerId",
                schema: "ipl",
                table: "ReplacementRequests",
                column: "ReplacementPlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatterBowlerMatchups",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "PersonSeasonStats",
                schema: "ipl");

            migrationBuilder.DropTable(
                name: "ReplacementRequests",
                schema: "ipl");

            migrationBuilder.DropColumn(
                name: "ScorecardJson",
                schema: "ipl",
                table: "Matches");
        }
    }
}

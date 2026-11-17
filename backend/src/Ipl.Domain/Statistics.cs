namespace Ipl.Domain;

public enum CompetitionType { Franchise, Domestic, International }

public enum MatchFormat { T20, ListA, FirstClass, Test, ODI, T20I }

public class Competition
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public CompetitionType Type { get; set; }
    public MatchFormat Format { get; set; }
    /// <summary>Relative strength used by the rating model (1 = lowest).</summary>
    public int Level { get; set; }
    public bool IsDemo { get; set; }
}

public class Match : Entity
{
    public int CompetitionId { get; set; }
    public Competition? Competition { get; set; }
    public int Year { get; set; }
    public DateOnly Date { get; set; }
    public string Venue { get; set; } = "";
    public string Team1 { get; set; } = "";
    public string Team2 { get; set; } = "";
    public string? Result { get; set; }
    public string DataSource { get; set; } = "";
    public bool IsDemo { get; set; }
}

/// <summary>
/// One player's performance in one match, including phase splits (powerplay / middle / death),
/// pace-vs-spin and batting-hand splits used by the performance centre.
/// </summary>
public class PlayerMatchStatistic : Entity
{
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public string Team { get; set; } = "";
    public string Opponent { get; set; } = "";
    public string Venue { get; set; } = "";
    public bool IsHome { get; set; }

    // Batting
    public bool Batted { get; set; }
    public int? BattingPosition { get; set; }
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public bool Dismissed { get; set; }
    public string? DismissalType { get; set; }
    public string? DismissedByBowlingType { get; set; }
    public int PowerplayRuns { get; set; }
    public int PowerplayBalls { get; set; }
    public int MiddleRuns { get; set; }
    public int MiddleBalls { get; set; }
    public int DeathRuns { get; set; }
    public int DeathBalls { get; set; }
    public int RunsVsPace { get; set; }
    public int BallsVsPace { get; set; }
    public int RunsVsSpin { get; set; }
    public int BallsVsSpin { get; set; }

    // Bowling
    public bool Bowled { get; set; }
    public int BallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int Maidens { get; set; }
    public int DotBalls { get; set; }
    public int PowerplayBallsBowled { get; set; }
    public int PowerplayRunsConceded { get; set; }
    public int PowerplayWickets { get; set; }
    public int MiddleBallsBowled { get; set; }
    public int MiddleRunsConceded { get; set; }
    public int MiddleWickets { get; set; }
    public int DeathBallsBowled { get; set; }
    public int DeathRunsConceded { get; set; }
    public int DeathWickets { get; set; }
    public int WicketsVsRightHand { get; set; }
    public int WicketsVsLeftHand { get; set; }

    // Fielding
    public int Catches { get; set; }
    public int Stumpings { get; set; }
    public int RunOuts { get; set; }
}

/// <summary>Batting/bowling/fielding totals; shared by season and career rows.</summary>
public abstract class StatisticTotals : Entity
{
    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }
    public int Matches { get; set; }
    public int Innings { get; set; }
    public int NotOuts { get; set; }
    public int Runs { get; set; }
    public int BallsFaced { get; set; }
    public int HighestScore { get; set; }
    public bool HighestNotOut { get; set; }
    public int Hundreds { get; set; }
    public int Fifties { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int BowlingInnings { get; set; }
    public int BallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int BestBowlingWickets { get; set; }
    public int BestBowlingRuns { get; set; }
    public int FourWicketHauls { get; set; }
    public int FiveWicketHauls { get; set; }
    public int Catches { get; set; }
    public int Stumpings { get; set; }
    /// <summary>Provenance: provider, CSV import or "Demo generator (synthetic)".</summary>
    public string DataSource { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; }
}

public class PlayerSeasonStatistic : StatisticTotals
{
    public int CompetitionId { get; set; }
    public Competition? Competition { get; set; }
    public int Year { get; set; }
    public string? TeamName { get; set; }
}

public class PlayerCareerStatistic : StatisticTotals
{
    public int CompetitionId { get; set; }
    public Competition? Competition { get; set; }
    public int FirstYear { get; set; }
    public int LastYear { get; set; }
}

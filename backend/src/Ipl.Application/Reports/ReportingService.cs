using Ipl.Application.Analytics;
using Ipl.Application.Auctions;
using Ipl.Application.Finance;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Reports;

// ---------- Dapper-backed read models (implemented in Infrastructure) ----------

public record MonthlySpendRow(int Year, int Month, string Category, decimal Total);
public record PerformerRow(Guid PlayerId, string Name, string Role, int Matches, int Runs, int BallsFaced, int Wickets, int BallsBowled, int RunsConceded);
public record StandingRow(Guid FranchiseId, string Franchise, string ShortName, string PrimaryColor, int Played, int Won, int Lost, int NoResult, int Points, decimal? NetRunRate, int? FinalPosition);
public record StatusCountRow(string Status, int Count);
public record AuctionSpendRow(Guid FranchiseId, string Franchise, int Players, decimal Spent, decimal MaxPrice);

public interface IReportQueries
{
    Task<IReadOnlyList<MonthlySpendRow>> MonthlySpendAsync(Guid franchiseId, int seasonId, CancellationToken ct);
    Task<IReadOnlyList<PerformerRow>> SquadPerformersAsync(Guid franchiseId, int seasonId, int year, CancellationToken ct);
    Task<IReadOnlyList<StandingRow>> StandingsAsync(int seasonId, CancellationToken ct);
    Task<IReadOnlyList<StatusCountRow>> TradeStatusCountsAsync(Guid? franchiseId, int seasonId, CancellationToken ct);
    Task<IReadOnlyList<AuctionSpendRow>> AuctionSpendAsync(int seasonId, CancellationToken ct);
}

// ---------- Exports ----------

public record ReportTable(string Title, string? Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, string? Footnote = null);

public interface IExportService
{
    byte[] ToCsv(ReportTable table);
    byte[] ToExcel(ReportTable table);
    byte[] ToPdf(ReportTable table);
}

// ---------- Dashboards ----------

public record DeadlineDto(string Title, DateOnly Date, int DaysLeft, string Link);

public record ApprovalRequestDto(Guid TradeId, string Reference, string Type, string Counterparty, DateTime? Since);

public record OwnerDashboardDto(decimal SquadValue, decimal PurseRemaining, decimal PurseTotal, int OpenTrades, int CompletedTrades, int AwaitingMyResponse,
    bool RetentionSubmitted, DateOnly? RetentionDeadline, decimal TeamStrength, int StrengthRank, IReadOnlyList<DeadlineDto> Deadlines,
    IReadOnlyList<ApprovalRequestDto> ApprovalRequests, IReadOnlyList<StatusCountRow> TradeStatus);

public record DirectorDashboardDto(CompositionDto Composition, IReadOnlyList<SquadPlayerDto> TopRated, IReadOnlyList<SquadNeedDto> Needs,
    IReadOnlyList<RecentReportDto> ScoutingRecommendations, IReadOnlyList<ValueGapDto> TradeOpportunities, IReadOnlyList<AuctionTargetDto> AuctionTargets,
    decimal SquadModelValue, decimal SquadPrice);

public record RecentReportDto(Guid Id, string Player, string Scout, int Rating, string Recommendation, DateOnly ObservedOn);

public record CoachDashboardDto(int Available, int Injured, int Doubtful, int Unavailable, IReadOnlyList<SquadPlayerDto> InForm, IReadOnlyList<SquadPlayerDto> OutOfForm,
    string? PreferredXI, IReadOnlyList<string> XIWarnings, IReadOnlyList<PerformerRow> TopRunScorers, IReadOnlyList<PerformerRow> TopWicketTakers, CompositionDto Balance);

public record FinanceDashboardDto(decimal PurseTotal, decimal PurseRemaining, decimal TotalSpent, decimal ContractCommitments, decimal TradeConsiderationNet,
    IReadOnlyList<LedgerSummaryDto> Ledgers, IReadOnlyList<MonthlySpendRow> MonthlySpend, IReadOnlyList<TransactionDto> RecentTransactions);

public record DashboardDto(Guid? FranchiseId, string? Franchise, string Season, bool IsLeagueView, OwnerDashboardDto? Owner, DirectorDashboardDto? Director,
    CoachDashboardDto? Coach, FinanceDashboardDto? Finance, LeagueDashboardDto? League);

public record LeagueDashboardDto(IReadOnlyList<StandingRow> Standings, IReadOnlyList<AuctionSpendRow> AuctionSpend, IReadOnlyList<StatusCountRow> Trades,
    int PendingLeagueApprovals, int Players, int FreeAgents);

public class DashboardService(
    IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IReportQueries reports, SquadService squads,
    FinanceService finance, RecommendationService recommendations, RatingService ratings)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        me.Require(Perm.ReportsRead);
        var season = await rules.CurrentSeasonAsync(ct);
        if (me.FranchiseId is not { } fid)
        {
            var standings = await reports.StandingsAsync(season.Id, ct);
            return new DashboardDto(null, null, season.Name, true, null, null, null, null, new LeagueDashboardDto(
                standings, await reports.AuctionSpendAsync(season.Id, ct), await reports.TradeStatusCountsAsync(null, season.Id, ct),
                await db.TradeOffers.CountAsync(t => t.Status == TradeStatus.AwaitingLeagueApproval, ct),
                await db.Players.CountAsync(ct), await db.Players.CountAsync(p => p.CurrentFranchiseId == null && p.Status == PlayerStatus.AvailableForAuction, ct)));
        }
        var franchise = await db.Franchises.AsNoTracking().Where(f => f.Id == fid).Select(f => f.Name).FirstAsync(ct);
        var squad = me.Has(Perm.SquadRead) ? await squads.GetAsync(fid, ct) : null;

        OwnerDashboardDto? owner = null;
        if (me.Has(Perm.TradesApprove) || me.Has(Perm.FinanceApprove))
            owner = await OwnerAsync(fid, season, squad, ct);
        DirectorDashboardDto? director = null;
        if (me.Has(Perm.SquadManage) && me.Has(Perm.ValuationRead) && squad is not null)
            director = await DirectorAsync(fid, squad, ct);
        CoachDashboardDto? coach = null;
        if (me.Has(Perm.SquadRead) && squad is not null)
            coach = await CoachAsync(fid, season, squad, ct);
        FinanceDashboardDto? fin = null;
        if (me.Has(Perm.FinanceRead))
            fin = await FinanceAsync(fid, season.Id, ct);
        return new DashboardDto(fid, franchise, season.Name, false, owner, director, coach, fin, null);
    }

    private async Task<OwnerDashboardDto> OwnerAsync(Guid fid, Season season, SquadDto? squad, CancellationToken ct)
    {
        var r = await rules.ForSeasonAsync(season.Id, ct);
        var fs = await db.FranchiseSeasons.AsNoTracking().FirstAsync(s => s.FranchiseId == fid && s.SeasonId == season.Id, ct);
        var trades = await db.TradeOffers.AsNoTracking().Where(t => (t.InitiatorFranchiseId == fid || t.CounterpartyFranchiseId == fid) && t.SeasonId == season.Id)
            .Select(t => new { t.Status, t.AwaitingFranchiseId, t.InitiatorFranchiseId }).ToListAsync(ct);
        var approvals = await db.TradeApprovals.AsNoTracking()
            .Where(a => a.FranchiseId == fid && a.Decision == ApprovalDecision.Pending && a.Type == ApprovalType.FranchiseAuthorization)
            .Select(a => new
            {
                a.TradeOfferId, a.TradeOffer!.Reference, a.Type, a.TradeOffer.InitiatorFranchiseId, a.TradeOffer.CounterpartyFranchiseId, a.TradeOffer.UpdatedAt,
                Initiator = a.TradeOffer.InitiatorFranchise!.Name, Counter = a.TradeOffer.CounterpartyFranchise!.Name,
            }).ToListAsync(ct);
        var deadlines = new List<DeadlineDto>();
        var today = clock.Today;
        void Add(string title, DateOnly? date, string link) { if (date is { } d && d >= today) deadlines.Add(new DeadlineDto(title, d, d.DayNumber - today.DayNumber, link)); }
        Add("Retention deadline", r.DateOrNull(RuleKeys.RetentionDeadline), "/retention");
        Add("Trade window closes", r.DateOrNull(RuleKeys.TradeWindowClose), "/trading");
        foreach (var a in await db.AuctionEvents.AsNoTracking().Where(a => a.SeasonId == season.Id && a.ScheduledAt != null && a.Status != AuctionStatus.Completed).ToListAsync(ct))
            Add(a.Name, DateOnly.FromDateTime(a.ScheduledAt!.Value), $"/auction/{a.Id}");
        Add("Season starts", season.StartDate, "/squad");

        // League strength rank: rate every active squad in one batch, then compare.
        decimal strength = squad?.Strength.Strength ?? 0;
        var all = await db.FranchiseSquads.AsNoTracking().Where(s => s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active && s.Franchise!.IsActive)
            .Select(s => new { s.FranchiseId, s.PlayerId, s.Player!.IsOverseas, s.Player.IsWicketKeeper, Role = s.Player.PrimaryRole!.Code, s.Player.BowlingType }).ToListAsync(ct);
        var rated = await ratings.RateManyAsync(all.Select(x => x.PlayerId).ToList(), ct);
        var maxOs = r.Int(RuleKeys.MaxOverseasInXI);
        var minBowl = r.IntOr(RuleKeys.MinBowlingOptions, 5);
        var rank = 1 + all.GroupBy(x => x.FranchiseId).Where(g => g.Key != fid).Count(g => RatingService.TeamStrength(
            g.Select(x => (x.PlayerId, rated.TryGetValue(x.PlayerId, out var rt) ? rt.Rating : 40m, x.IsOverseas, x.IsWicketKeeper, SquadMath.IsBowlingOption(x.Role, x.BowlingType))).ToList(),
            maxOs, minBowl).Strength > strength);

        return new OwnerDashboardDto(squad?.SquadValue ?? 0, fs.PurseRemaining, fs.PurseTotal,
            trades.Count(t => t.Status is not (TradeStatus.Completed or TradeStatus.Rejected or TradeStatus.Withdrawn or TradeStatus.Expired)),
            trades.Count(t => t.Status == TradeStatus.Completed),
            trades.Count(t => t.AwaitingFranchiseId == fid),
            await db.RetentionPlans.AnyAsync(p => p.FranchiseId == fid && p.SeasonId == season.Id && p.IsSubmitted, ct),
            r.DateOrNull(RuleKeys.RetentionDeadline), strength, rank, deadlines.OrderBy(d => d.Date).ToList(),
            approvals.Select(a => new ApprovalRequestDto(a.TradeOfferId, a.Reference, a.Type.ToString(), a.InitiatorFranchiseId == fid ? a.Counter : a.Initiator, a.UpdatedAt)).ToList(),
            await reports.TradeStatusCountsAsync(fid, season.Id, ct));
    }

    private async Task<DirectorDashboardDto> DirectorAsync(Guid fid, SquadDto squad, CancellationToken ct)
    {
        var reportsList = me.Has(Perm.ScoutingRead)
            ? await db.PlayerScoutingReports.AsNoTracking().Where(r => r.FranchiseId == fid && r.Status != ReportStatus.Draft
                    && (r.Recommendation == ScoutingRecommendation.Sign || r.Recommendation == ScoutingRecommendation.StrongMonitor))
                .OrderByDescending(r => r.ObservedOn).Take(6)
                .Select(r => new RecentReportDto(r.Id, r.Player!.FullName, r.Scout!.FullName, r.OverallRating, r.Recommendation.ToString(), r.ObservedOn)).ToListAsync(ct)
            : [];
        var gaps = (await recommendations.ValueGapsAsync(true, ct)).Where(g => g.Franchise != squad.FranchiseName).Take(6).ToList();
        var strategy = await recommendations.AuctionStrategyAsync(fid, ct);
        return new DirectorDashboardDto(squad.Composition, squad.Players.OrderByDescending(p => p.Rating).Take(8).ToList(),
            strategy.Needs, reportsList, gaps, strategy.Targets.Take(6).ToList(), 0, squad.SquadValue);
    }

    private async Task<CoachDashboardDto> CoachAsync(Guid fid, Season season, SquadDto squad, CancellationToken ct)
    {
        var xi = await db.PlayingXIs.AsNoTracking().Where(x => x.FranchiseId == fid && x.SeasonId == season.Id)
            .OrderByDescending(x => x.IsPreferred).ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt).Select(x => x.Id).FirstOrDefaultAsync(ct);
        string? xiName = null;
        IReadOnlyList<string> warnings = [];
        if (xi != Guid.Empty)
        {
            var dto = (await squads.ListXIsAsync(ct)).FirstOrDefault(x => x.Id == xi);
            xiName = dto?.Name;
            warnings = dto?.Warnings ?? [];
        }
        var lastYear = await db.PlayerSeasonStatistics.AsNoTracking().MaxAsync(s => (int?)s.Year, ct) ?? season.Year - 1;
        var performers = await reports.SquadPerformersAsync(fid, season.Id, lastYear, ct);
        return new CoachDashboardDto(
            squad.Players.Count(p => p.Availability == "Available"), squad.Players.Count(p => p.Availability == "Injured"),
            squad.Players.Count(p => p.Availability == "Doubtful"), squad.Players.Count(p => p.Availability is "Unavailable" or "NationalDuty" or "Personal"),
            squad.Players.Where(p => p.FormTrend == "Rising").OrderByDescending(p => p.Rating).Take(6).ToList(),
            squad.Players.Where(p => p.FormTrend == "Falling").OrderBy(p => p.Rating).Take(6).ToList(),
            xiName, warnings, performers.OrderByDescending(p => p.Runs).Take(5).ToList(), performers.OrderByDescending(p => p.Wickets).Take(5).ToList(), squad.Composition);
    }

    private async Task<FinanceDashboardDto> FinanceAsync(Guid fid, int seasonId, CancellationToken ct)
    {
        var overview = await finance.OverviewAsync(fid, ct);
        var recent = await finance.TransactionsAsync(fid, null, new PageRequest(1, 8), ct);
        var trade = overview.Ledgers.First(l => l.Category == nameof(FinanceCategory.TradeConsideration));
        return new FinanceDashboardDto(overview.PurseTotal, overview.PurseRemaining, overview.Ledgers.Sum(l => l.Debits), overview.ContractCommitments,
            trade.Net, overview.Ledgers, await reports.MonthlySpendAsync(fid, seasonId, ct), recent.Items);
    }
}

/// <summary>Builds exportable tables for the reports area.</summary>
public class ExportReportsService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, SquadService squads, AuctionService auctions, FinanceService finance)
{
    public static readonly string[] Types = ["squad", "auction-results", "finance-ledger", "contracts", "trades", "player-stats"];

    public async Task<ReportTable> BuildAsync(string type, Guid? id, CancellationToken ct)
    {
        me.Require(Perm.ReportsExport);
        var stamp = $"Generated {clock.UtcNow:yyyy-MM-dd HH:mm} UTC by {me.Email}";
        switch (type)
        {
            case "squad":
            {
                var fid = me.RequireFranchise(id);
                var s = await squads.GetAsync(fid, ct);
                return new ReportTable($"{s.FranchiseName} — Squad", $"{s.Season} · {s.Composition.Total} players · strength {s.Strength.Strength}",
                    ["Player", "Role", "Age", "Overseas", "Capped", "Acquisition", "Price (₹)", "Availability", "Rating"],
                    s.Players.Select(p => (IReadOnlyList<object?>)[p.Name, p.Role, p.Age, p.IsOverseas ? "Yes" : "No", p.IsCapped ? "Yes" : "No", p.Acquisition, p.Price, p.Availability, p.Rating]).ToList(),
                    $"{stamp}. Ratings are model estimates.");
            }
            case "auction-results":
            {
                var auctionId = id ?? await db.AuctionEvents.OrderByDescending(a => a.CreatedAt).Select(a => a.Id).FirstOrDefaultAsync(ct);
                if (auctionId == Guid.Empty) throw new NotFoundException("Auction");
                var r = await auctions.ResultsAsync(auctionId, ct);
                return new ReportTable($"{r.Name} — Results", $"{r.Sold} sold · {r.Unsold} unsold · total {Money.Format(r.TotalSpent)}",
                    ["Player", "Role", "Overseas", "Outcome", "Franchise", "Price (₹)", "Base (₹)", "Round"],
                    r.SoldPlayers.Concat(r.UnsoldPlayers).Select(x => (IReadOnlyList<object?>)[x.Player, x.Role, x.IsOverseas ? "Yes" : "No", x.Outcome, x.Franchise, x.Price, x.BasePrice, x.Round]).ToList(), stamp);
            }
            case "finance-ledger":
            {
                me.Require(Perm.FinanceRead);
                var fid = me.RequireFranchise(id);
                var rows = await finance.TransactionsAsync(fid, null, new PageRequest(1, 200), ct);
                var name = await db.Franchises.Where(f => f.Id == fid).Select(f => f.Name).FirstAsync(ct);
                return new ReportTable($"{name} — Finance ledger", "All categories, newest first",
                    ["Date", "Category", "Direction", "Amount (₹)", "Purse after (₹)", "Description"],
                    rows.Items.Select(t => (IReadOnlyList<object?>)[t.CreatedAt, t.Category, t.Direction, t.Amount, t.PurseBalanceAfter, t.Description]).ToList(), stamp);
            }
            case "contracts":
            {
                var fid = me.RequireFranchise(id);
                var rows = await finance.ContractsAsync(fid, null, ct);
                return new ReportTable("Player contracts", null, ["Player", "Role", "From", "To", "Annual salary (₹)", "Acquisition", "Status"],
                    rows.Select(c => (IReadOnlyList<object?>)[c.Player, c.Role, c.StartSeason, c.EndSeason, c.AnnualSalary, c.Acquisition, c.Status]).ToList(), stamp);
            }
            case "trades":
            {
                me.Require(Perm.TradesRead);
                var fid = me.FranchiseId;
                var q = db.TradeOffers.AsNoTracking().Where(t => t.Status != TradeStatus.Draft);
                if (!me.IsLeagueWide) q = q.Where(t => t.InitiatorFranchiseId == fid || t.CounterpartyFranchiseId == fid);
                var rows = await q.OrderByDescending(t => t.CreatedAt).Select(t => new { t.Reference, t.Status, I = t.InitiatorFranchise!.ShortName, C = t.CounterpartyFranchise!.ShortName, Players = t.Players.Count, t.CashConsideration, t.CreatedAt }).ToListAsync(ct);
                return new ReportTable("Trades", null, ["Reference", "Status", "Initiator", "Counterparty", "Players", "Consideration (₹)", "Created"],
                    rows.Select(t => (IReadOnlyList<object?>)[t.Reference, t.Status.ToString(), t.I, t.C, t.Players, t.CashConsideration, t.CreatedAt]).ToList(), stamp);
            }
            case "player-stats":
            {
                me.Require(Perm.StatsRead);
                var pid = id ?? throw new AppValidationException("id", "Choose a player.");
                var name = await db.Players.Where(p => p.Id == pid).Select(p => p.FullName).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Player");
                var rows = await db.PlayerSeasonStatistics.AsNoTracking().Where(s => s.PlayerId == pid).OrderByDescending(s => s.Year)
                    .Select(s => new { s.Year, Comp = s.Competition!.Name, s.Matches, s.Runs, s.BallsFaced, s.Innings, s.NotOuts, s.Wickets, s.BallsBowled, s.RunsConceded, s.DataSource, s.LastUpdatedAt })
                    .ToListAsync(ct);
                return new ReportTable($"{name} — Season statistics", null,
                    ["Year", "Competition", "M", "Runs", "Avg", "SR", "Wkts", "Econ", "Source", "Updated"],
                    rows.Select(s => (IReadOnlyList<object?>)[s.Year, s.Comp, s.Matches, s.Runs,
                        s.Innings - s.NotOuts > 0 ? Math.Round((decimal)s.Runs / (s.Innings - s.NotOuts), 2) : null,
                        s.BallsFaced > 0 ? Math.Round(s.Runs * 100m / s.BallsFaced, 2) : null, s.Wickets,
                        s.BallsBowled > 0 ? Math.Round(s.RunsConceded * 6m / s.BallsBowled, 2) : null, s.DataSource, s.LastUpdatedAt]).ToList(), stamp);
            }
            default:
                throw new AppValidationException("type", $"Unknown report. Use one of: {string.Join(", ", Types)}.");
        }
    }
}

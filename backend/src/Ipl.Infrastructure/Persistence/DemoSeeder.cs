using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Ipl.Application.Identity;
using Ipl.Application.Rules;
using Ipl.Application.Statistics;
using Ipl.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Ipl.Infrastructure.Persistence;

public class DemoSeedOptions
{
    /// <summary>Create the fictional demo league on an empty database.</summary>
    public bool Enabled { get; set; }
    /// <summary>Number of completed past seasons to simulate (1–5). Tests use fewer to stay fast.</summary>
    public int HistoricSeasons { get; set; } = 5;
    /// <summary>Password for every demo account (development only).</summary>
    public string Password { get; set; } = "Demo@2026pass";
}

/// <summary>
/// Builds a complete, clearly fictional demo league. Every name is invented, every franchise and competition is
/// marked IsDemo, and every statistic is generated (DataSource says so). Nothing here is real cricket data.
/// </summary>
public class DemoSeeder(AppDbContext db, IPasswordHasher<User> hasher, DemoSeedOptions options, ILogger<DemoSeeder> log)
{
    public const string Source = "Demo generator (synthetic, fictional player) — not real statistics";
    private readonly Rng rng = new(2026);
    private readonly DateTime now = DateTime.UtcNow;
    private DateOnly Today => DateOnly.FromDateTime(now);

    private sealed class Profile
    {
        public required Player Player;
        public required string Role;
        public double Bat;
        public double Bowl;
        public bool CanBowl;
        public bool IsSpinner;
        public Franchise? Franchise;
        public decimal Price;
        public int DplDebut;
        public Dictionary<int, Franchise> History = [];
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Franchises.IgnoreQueryFilters().AnyAsync(ct)) return;
        // One transaction: a failure part-way leaves the database exactly as it was.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await SeedCoreAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task SeedCoreAsync(CancellationToken ct)
    {
        var roles = await db.PlayerRoles.ToDictionaryAsync(r => r.Code, ct);
        var countries = await db.Countries.ToDictionaryAsync(c => c.Code, ct);

        // ---------------- Competitions & seasons ----------------
        // Real league entries are reference data; the demo-prospect competitions are clearly fictional.
        Competition C(string code, string name, CompetitionType t, MatchFormat f, int level, bool demo) => new() { Code = code, Name = name, Type = t, Format = f, Level = level, IsDemo = demo };
        db.Competitions.AddRange(
            C("IPL", "Indian Premier League", CompetitionType.Franchise, MatchFormat.T20, 5, false),
            C("SMAT", "Syed Mushtaq Ali Trophy", CompetitionType.Domestic, MatchFormat.T20, 3, false),
            C("VHT", "Vijay Hazare Trophy", CompetitionType.Domestic, MatchFormat.ListA, 3, false),
            C("RANJI", "Ranji Trophy", CompetitionType.Domestic, MatchFormat.FirstClass, 3, false),
            C("T20I", "T20 Internationals", CompetitionType.International, MatchFormat.T20I, 5, false),
            C("ODI", "One-Day Internationals", CompetitionType.International, MatchFormat.ODI, 5, false),
            C("TEST", "Test Matches", CompetitionType.International, MatchFormat.Test, 5, false));
        var demoT20 = C("DT20", "Demo Prospects T20 League (fictional)", CompetitionType.Domestic, MatchFormat.T20, 2, true);
        var demoOd = C("DODC", "Demo Prospects One-Day Cup (fictional)", CompetitionType.Domestic, MatchFormat.ListA, 2, true);
        db.Competitions.AddRange(demoT20, demoOd);

        var currentYear = Today.Year + (Today.Month >= 6 ? 1 : 0);
        var seasons = new Dictionary<int, Season>();
        foreach (var y in Enumerable.Range(2008, currentYear - 2008 + 1))
        {
            var s = new Season
            {
                Year = y, Name = $"IPL {y}", StartDate = new DateOnly(y, 3, 22), EndDate = new DateOnly(y, 5, 31), IsCurrent = y == currentYear,
                Status = y == currentYear ? SeasonStatus.Retention : SeasonStatus.Completed,
            };
            seasons[y] = s;
            db.Seasons.Add(s);
        }
        await db.SaveChangesAsync(ct);
        AddRules(seasons[currentYear], true);
        AddRules(seasons[currentYear - 1], false);

        // ---------------- Franchises ----------------
        var franchises = new Dictionary<string, Franchise>();
        foreach (var t in RealSquads.Teams)
        {
            var f = new Franchise
            {
                Code = t.Code, Name = t.Name, ShortName = t.Code, City = t.City, HomeGround = t.Ground, OwnerName = t.Owner, OwnerCompany = t.Owner,
                PrimaryColor = t.Primary, SecondaryColor = t.Secondary, FoundedYear = t.Founded, IsDemo = false, CreatedAt = now,
            };
            f.Staff.Add(new FranchiseStaff { Name = RealSquads.Captains[t.Code], Position = "Captain (IPL 2026)", Category = StaffCategory.Management });
            franchises[t.Code] = f;
            db.Franchises.Add(f);
        }
        await db.SaveChangesAsync(ct);

        // Season results: only the published finals (champion / runner-up). Other positions are not recorded.
        foreach (var (year, champion, runnerUp) in RealSquads.Finals)
            foreach (var (code, f) in franchises)
            {
                if (f.FoundedYear > year) continue;
                db.FranchiseSeasons.Add(new FranchiseSeason
                {
                    FranchiseId = f.Id, SeasonId = seasons[year].Id, IsChampion = code == champion, IsRunnerUp = code == runnerUp,
                    FinalPosition = code == champion ? 1 : code == runnerUp ? 2 : null,
                });
            }

        // ---------------- Real players (IPL 2026 squads) ----------------
        var squadSeason = seasons[currentYear - 1];
        var current = seasons[currentYear];
        var codeNo = 0;
        var realPlayers = new List<(Player Player, Franchise Franchise, decimal? Price, bool Signed)>();
        foreach (var line in RealSquads.Players.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var c = line.Split('|');
            var (team, name, role, country, capped, hand, style) = (c[0], c[1], c[2], c[3], c[4] == "1", c[5], c[6]);
            var roleCode = role switch { "bat" => "batter", "wk" => "wk_batter", "ar" => "allrounder", _ => "bowler" };
            var player = new Player
            {
                Code = $"P{++codeNo:00000}", FullName = name, DisplayName = name, DateOfBirth = null, CountryId = countries[country].Id,
                IsOverseas = country != "IND", IsCapped = capped, IsWicketKeeper = role == "wk",
                BattingHand = hand switch { "R" => BattingHand.RightHand, "L" => BattingHand.LeftHand, _ => BattingHand.Unknown },
                BowlingStyle = style == "?" ? "" : style, BowlingType = ParseBowlingType(style, roleCode),
                PrimaryRoleId = roles[roleCode].Id, Status = PlayerStatus.UnderContract, CareerStatus = CareerStatus.Active,
                CurrentFranchiseId = franchises[team].Id, IsDemo = false, DataSource = RealSquads.Source, CreatedAt = now,
            };
            db.Players.Add(player);
            realPlayers.Add((player, franchises[team], RealSquads.KnownPrices.TryGetValue(name, out var price) ? price : null, RealSquads.SignedAtAuction.Contains(name)));
        }

        // ---------------- Fictional demo prospects (auction pool and scouting practice) ----------------
        var prospects = new List<Profile>();
        for (var i = 0; i < 48; i++)
        {
            var role = rng.Pick(["batter", "batter", "wk_batter", "allrounder", "allrounder", "bowler", "bowler", "bowler"]);
            var overseas = rng.Chance(0.25);
            var cc = overseas ? rng.Pick(["AUS", "ENG", "RSA", "NZL", "WIN", "AFG", "SRI"]) : "IND";
            var age = rng.Int(18, 27);
            BowlingType bt = role is "bowler" or "allrounder"
                ? rng.Pick([BowlingType.Fast, BowlingType.MediumFast, BowlingType.OffSpin, BowlingType.LegSpin, BowlingType.LeftArmOrthodox])
                : BowlingType.None;
            var name = $"{(overseas ? Names.Overseas(rng, cc) : Names.Indian(rng))} (Demo)";
            var talent = Math.Clamp(rng.Normal(0.5, 0.18), 0.05, 0.95);
            var p = new Player
            {
                Code = $"D{i + 1:0000}", FullName = name, DisplayName = name, DateOfBirth = new DateOnly(Today.Year - age, rng.Int(1, 12), rng.Int(1, 28)),
                CountryId = countries[cc].Id, IsOverseas = overseas, BattingHand = rng.Chance(0.3) ? BattingHand.LeftHand : BattingHand.RightHand,
                BowlingType = bt, BowlingStyle = BowlingStyle(bt, rng), PrimaryRoleId = roles[role].Id, IsWicketKeeper = role == "wk_batter",
                IsCapped = false, Status = PlayerStatus.AvailableForAuction, CareerStatus = CareerStatus.Active, IsDemo = true, DataSource = Source, CreatedAt = now,
            };
            db.Players.Add(p);
            prospects.Add(new Profile
            {
                Player = p, Role = role, CanBowl = bt != BowlingType.None, IsSpinner = bt is BowlingType.OffSpin or BowlingType.LegSpin or BowlingType.LeftArmOrthodox,
                Bat = role switch { "bowler" => talent * 0.35, "allrounder" => talent * 0.8, _ => talent },
                Bowl = role switch { "bowler" => talent, "allrounder" => talent * 0.85, _ => talent * 0.3 },
            });
        }
        await db.SaveChangesAsync(ct);

        // Synthetic statistics for the fictional prospects only (never for real players).
        var matches = new List<Match>();
        var lines = new List<PlayerMatchStatistic>();
        string[] teams = ["Demo Northern XI", "Demo Coastal XI", "Demo Central XI", "Demo Eastern XI", "Demo Western XI", "Demo Southern XI"];
        string[] grounds = ["Demo Ground 1", "Demo Ground 2", "Demo Ground 3", "Demo Ground 4"];
        foreach (var p in prospects)
            foreach (var y in Enumerable.Range(currentYear - 3, 3))
            {
                Individual(p, demoT20, y, rng.Int(5, 9), MatchFormat.T20, teams, grounds, matches, lines);
                Individual(p, demoOd, y, rng.Int(3, 6), MatchFormat.ListA, teams, grounds, matches, lines);
            }
        await BulkInsertAsync(db, matches, "Matches", ct);
        await BulkInsertAsync(db, lines, "PlayerMatchStatistics", ct);
        var compByMatch = matches.ToDictionary(m => m.Id, m => (m.CompetitionId, m.Year));
        var seasonRows = new List<PlayerSeasonStatistic>();
        foreach (var g in lines.GroupBy(l => (l.PlayerId, compByMatch[l.MatchId].CompetitionId, compByMatch[l.MatchId].Year)))
        {
            var row = new PlayerSeasonStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId, Year = g.Key.Year, DataSource = Source, LastUpdatedAt = now, TeamName = g.First().Team };
            StatisticsService.Aggregate(row, g.ToList());
            seasonRows.Add(row);
        }
        db.PlayerSeasonStatistics.AddRange(seasonRows);
        foreach (var g in seasonRows.GroupBy(s => (s.PlayerId, s.CompetitionId)))
        {
            var career = new PlayerCareerStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId, FirstYear = g.Min(s => s.Year), LastYear = g.Max(s => s.Year), DataSource = Source, LastUpdatedAt = now };
            StatisticsService.Sum(career, g.ToList());
            db.PlayerCareerStatistics.Add(career);
        }

        // ---------------- Current season: squads carried forward from 2026, purses ----------------
        var purse = 120 * Money.Crore;
        foreach (var f in franchises.Values)
        {
            var squad = realPlayers.Where(r => r.Franchise == f).ToList();
            db.FranchiseSeasons.Add(new FranchiseSeason { FranchiseId = f.Id, SeasonId = current.Id, PurseTotal = purse, PurseRemaining = purse });
            db.FinanceTransactions.Add(new FinanceTransaction
            {
                FranchiseId = f.Id, SeasonId = current.Id, Category = FinanceCategory.AuctionPurse, Direction = FinanceDirection.Credit, Amount = purse,
                PurseBalanceAfter = purse, Description = $"{current.Name} auction purse allocation (rule value; set the official figure in Settings)",
                IdempotencyKey = $"purse-alloc:{f.Id}:{current.Id}", CreatedAt = now,
            });
            foreach (var r in squad)
            {
                var acquisition = r.Signed ? AcquisitionType.Auction : AcquisitionType.Initial;
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = r.Player.Id, FranchiseId = f.Id, SeasonId = squadSeason.Id, Acquisition = acquisition, Price = r.Price, FromDate = squadSeason.StartDate });
                db.FranchiseSquads.Add(new FranchiseSquad
                {
                    FranchiseId = f.Id, SeasonId = current.Id, PlayerId = r.Player.Id, Acquisition = acquisition, Price = r.Price ?? 0,
                    Status = SquadMemberStatus.Active, JoinedAt = now,
                });
            }
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Users ----------------
        var roleIds = await db.Roles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);
        User NewUser(string email, string name, string? role, Guid? franchiseId, string? title = null)
        {
            var u = new User { Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = name, IsActive = true, IsActivated = true, DefaultFranchiseId = franchiseId, CreatedAt = now };
            u.PasswordHash = hasher.HashPassword(u, options.Password);
            db.Users.Add(u);
            if (role is not null) db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = roleIds[role], FranchiseId = franchiseId, AssignedAt = now });
            if (franchiseId is { } fid) db.FranchiseMembers.Add(new FranchiseMember { FranchiseId = fid, UserId = u.Id, JobTitle = title ?? "", JoinedAt = now });
            return u;
        }
        static string Slug(string roleCode) => roleCode.Replace("franchise_", "").Replace("_", ".");
        NewUser("admin@demo.local", "Platform Administrator (demo)", RoleCatalog.SuperAdmin, null);
        var league = NewUser("league@demo.local", "League Operations (demo)", RoleCatalog.LeagueAdmin, null);
        var mi = franchises["MI"];
        var csk = franchises["CSK"];
        var users = new Dictionary<string, User>();
        foreach (var (code, name, _, _, _) in RoleCatalog.All.Where(r => r.Scope == RoleScope.Franchise))
            users[code] = NewUser($"{Slug(code)}.mi@demo.local", $"Demo {name} (MI)", code, mi.Id, name);
        var scout2 = NewUser("scout2.mi@demo.local", "Demo Scout 2 (MI)", RoleCatalog.Scout, mi.Id, "Scout");
        foreach (var code in new[] { RoleCatalog.Owner, RoleCatalog.Ceo, RoleCatalog.CricketDirector, RoleCatalog.AuctionManager, RoleCatalog.Operations })
            NewUser($"{Slug(code)}.csk@demo.local", $"Demo {RoleCatalog.All.First(r => r.Code == code).Name} (CSK)", code, csk.Id, RoleCatalog.All.First(r => r.Code == code).Name);
        foreach (var f in franchises.Values.Where(f => f.Code is not ("MI" or "CSK")))
        {
            NewUser($"owner.{f.Code.ToLowerInvariant()}@demo.local", $"Demo Owner ({f.Code})", RoleCatalog.Owner, f.Id, "Owner");
            NewUser($"auction.{f.Code.ToLowerInvariant()}@demo.local", $"Demo Auction Manager ({f.Code})", RoleCatalog.AuctionManager, f.Id, "Auction Manager");
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Practice auction with the fictional prospects ----------------
        var auction = new AuctionEvent
        {
            SeasonId = current.Id, Name = $"{current.Name} practice auction (demo prospects)", IsMegaAuction = false, ScheduledAt = now.Date.AddDays(14).AddHours(9),
            Status = AuctionStatus.Ready, BidTimerSeconds = 30, AllowReopenUnsold = true, CreatedAt = now, CreatedBy = league.Id,
        };
        db.AuctionEvents.Add(auction);
        var lot = 0;
        var setNo = 0;
        foreach (var g in prospects.GroupBy(p => p.Role))
        {
            setNo++;
            foreach (var p in g.OrderByDescending(p => Math.Max(p.Bat, p.Bowl)))
            {
                var t = Math.Max(p.Bat, p.Bowl);
                db.AuctionPlayers.Add(new AuctionPlayer
                {
                    AuctionEventId = auction.Id, PlayerId = p.Player.Id, SetName = $"Demo {RoleName(g.Key)}s", SetNumber = setNo, LotNumber = ++lot,
                    BasePrice = t > 0.65 ? 75 * Money.Lakh : t > 0.45 ? 40 * Money.Lakh : 30 * Money.Lakh, Status = AuctionLotStatus.Pending,
                });
            }
        }

        // ---------------- Scouting practice (fictional prospects only) ----------------
        var scout = users[RoleCatalog.Scout];
        var emerging = prospects.OrderByDescending(p => Math.Max(p.Bat, p.Bowl)).Take(3).ToList();
        var assignment = new ScoutingAssignment
        {
            FranchiseId = mi.Id, PlayerId = emerging[0].Player.Id, Title = $"Watch {emerging[0].Player.FullName}", AssignedToUserId = scout.Id,
            Description = "Practice assignment on a fictional demo prospect.", DueDate = Today.AddDays(10), Status = AssignmentStatus.InProgress,
            CreatedAt = now.AddDays(-5), CreatedBy = users[RoleCatalog.ScoutingManager].Id,
        };
        db.ScoutingAssignments.Add(assignment);
        for (var i = 0; i < emerging.Count; i++)
            db.PlayerScoutingReports.Add(new PlayerScoutingReport
            {
                FranchiseId = mi.Id, PlayerId = emerging[i].Player.Id, ScoutUserId = i == 2 ? scout2.Id : scout.Id, AssignmentId = i == 0 ? assignment.Id : null,
                MatchObserved = "Demo Prospects T20 League (fictional)", ObservedOn = Today.AddDays(-rng.Int(3, 40)),
                Strengths = "Practice report on a fictional player.", Weaknesses = "Practice report on a fictional player.", Observations = "Demo data.",
                TechniqueRating = rng.Int(6, 9), TemperamentRating = rng.Int(5, 9), FitnessRating = rng.Int(5, 8), PotentialRating = rng.Int(7, 10), OverallRating = rng.Int(6, 9),
                Recommendation = i == 0 ? ScoutingRecommendation.Sign : ScoutingRecommendation.StrongMonitor, Status = i == 1 ? ReportStatus.Reviewed : ReportStatus.Submitted,
                ReviewedBy = i == 1 ? users[RoleCatalog.ScoutingManager].Id : null, ReviewedAt = i == 1 ? now.AddDays(-1) : null,
                ReviewComment = i == 1 ? "Practice review." : null, CreatedAt = now.AddDays(-rng.Int(1, 20)),
            });

        var thread = new MessageThread { Subject = "Welcome to the demo", Type = ThreadType.Internal, FranchiseId = mi.Id, LastMessageAt = now, CreatedAt = now };
        thread.Messages.Add(new Message { SenderUserId = users[RoleCatalog.CricketDirector].Id, SenderName = users[RoleCatalog.CricketDirector].FullName, SenderFranchiseId = mi.Id,
            Body = "Squads are the published IPL 2026 lists. Prices, dates of birth and statistics are not loaded yet: import them from a licensed provider.", SentAt = now });
        db.MessageThreads.Add(thread);
        db.AuditLogs.Add(new AuditLog { At = now, Action = "system.reference_data_seeded", EntityType = "System", Details = "{\"note\":\"Real franchises and IPL 2026 squads; fictional demo prospects\"}" });
        await db.SaveChangesAsync(ct);
        log.LogInformation("League ready: {Real} real players, {Prospects} demo prospects.", realPlayers.Count, prospects.Count);
    }

    /// <summary>Bowling type from the published style text ("?" = bowls but style not recorded).</summary>
    private static BowlingType ParseBowlingType(string style, string roleCode)
    {
        var s = style.ToLowerInvariant();
        if (s == "?") return BowlingType.Unknown;
        if (s.Length == 0) return roleCode is "bowler" or "allrounder" ? BowlingType.Unknown : BowlingType.None;
        if (s.Contains("wrist")) return BowlingType.LeftArmWristSpin;
        if (s.Contains("orthodox")) return BowlingType.LeftArmOrthodox;
        if (s.Contains("leg-break")) return BowlingType.LegSpin;
        if (s.Contains("off-break")) return BowlingType.OffSpin;
        if (s.Contains("fast-medium") || s.Contains("medium-fast")) return BowlingType.MediumFast;
        if (s.Contains("fast")) return BowlingType.Fast;
        if (s.Contains("medium")) return BowlingType.Medium;
        return BowlingType.Unknown;
    }

    // ------------------------------------------------------------------ rules

    private void AddRules(Season s, bool current)
    {
        var today = Today;
        string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var values = new Dictionary<string, string>
        {
            [RuleKeys.MaxSquadSize] = "25",
            [RuleKeys.MinSquadSize] = "18",
            [RuleKeys.MaxOverseasInSquad] = "8",
            [RuleKeys.MaxOverseasInXI] = "4",
            [RuleKeys.MinBowlingOptions] = "5",
            [RuleKeys.ImpactPlayerEnabled] = "true",
            [RuleKeys.ImpactSubstitutes] = "5",
            [RuleKeys.AuctionPurse] = (120 * Money.Crore).ToString(CultureInfo.InvariantCulture),
            [RuleKeys.BidIncrements] = JsonSerializer.Serialize(new[]
            {
                new BidIncrementTier(Money.Crore, 5 * Money.Lakh), new BidIncrementTier(2 * Money.Crore, 10 * Money.Lakh),
                new BidIncrementTier(5 * Money.Crore, 20 * Money.Lakh), new BidIncrementTier(null, 25 * Money.Lakh),
            }, JsonOptions.Default),
            [RuleKeys.MinBasePrice] = (20 * Money.Lakh).ToString(CultureInfo.InvariantCulture),
            [RuleKeys.ReserveForMinimumSquad] = "true",
            [RuleKeys.RetentionDeadline] = current ? D(today.AddDays(21)) : D(s.StartDate.AddDays(-90)),
            [RuleKeys.MaxRetentions] = "6",
            [RuleKeys.MaxCappedRetentions] = "5",
            [RuleKeys.MaxUncappedRetentions] = "2",
            [RuleKeys.MaxOverseasRetentions] = "3",
            [RuleKeys.CappedRetentionCosts] = JsonSerializer.Serialize(new[] { 18 * Money.Crore, 14 * Money.Crore, 11 * Money.Crore, 18 * Money.Crore, 14 * Money.Crore }),
            [RuleKeys.UncappedRetentionCost] = (4 * Money.Crore).ToString(CultureInfo.InvariantCulture),
            [RuleKeys.TradeWindowOpen] = current ? D(today.AddDays(-14)) : D(s.StartDate.AddDays(-150)),
            [RuleKeys.TradeWindowClose] = current ? D(today.AddDays(30)) : D(s.StartDate.AddDays(-7)),
            [RuleKeys.TradeOfferExpiryDays] = "7",
            [RuleKeys.TradeRequiresLeagueApproval] = "true",
            [RuleKeys.TradeRequiresPlayerConsent] = "true",
            [RuleKeys.TradeConsiderationAffectsPurse] = "false",
            [RuleKeys.TradeSalaryAffectsPurse] = "true",
        };
        foreach (var def in RuleKeys.Definitions)
            db.SeasonRules.Add(new SeasonRule
            {
                SeasonId = s.Id, Key = def.Key, Value = values[def.Key], ValueType = def.Type, Category = def.Category,
                Description = def.Description + " (demo default — set the official value for each season)", UpdatedAt = now,
            });
    }

    // ------------------------------------------------------------------ simulation

    private List<Profile> PickXI(List<Profile> squad)
    {
        var xi = new List<Profile>();
        var ordered = squad.OrderByDescending(p => Math.Max(p.Bat, p.Bowl) + rng.Double(0, 0.15)).ToList();
        void Take(Profile p) { if (!xi.Contains(p) && (!p.Player.IsOverseas || xi.Count(x => x.Player.IsOverseas) < 4) && xi.Count < 11) xi.Add(p); }
        if (ordered.FirstOrDefault(p => p.Player.IsWicketKeeper) is { } wk) Take(wk);
        foreach (var p in ordered.Where(p => p.CanBowl && p.Role != "batter").Take(5)) Take(p);
        foreach (var p in ordered) Take(p);
        return xi.OrderByDescending(p => p.Bat).ToList();
    }

    /// <summary>Generates every XI member's line for one innings + bowling; returns the team total.</summary>
    private int SimulateTeam(Match m, List<Profile> xi, string team, string opponent, bool home, MatchFormat format, List<PlayerMatchStatistic> lines)
    {
        var total = 8; // extras
        if (xi.Count == 0) return total;
        var bowlers = xi.Where(p => p.CanBowl).OrderByDescending(p => p.Bowl).Take(6).ToList();
        if (bowlers.Count == 0) bowlers = xi.Take(5).ToList();
        var oversEach = Enumerable.Repeat(0, bowlers.Count).ToArray();
        var cap = Math.Max(4, (int)Math.Ceiling(20.0 / bowlers.Count));
        for (var o = 0; o < 20; o++) { var idx = Enumerable.Range(0, bowlers.Count).Where(i => oversEach[i] < cap).OrderBy(i => oversEach[i]).ThenBy(_ => rng.Next()).First(); oversEach[idx]++; }
        for (var pos = 0; pos < xi.Count; pos++)
        {
            var p = xi[pos];
            var line = NewLine(m, p, team, opponent, home);
            if (pos < 7 || rng.Chance(0.45)) Bat(line, p, pos + 1, format);
            var bIdx = bowlers.IndexOf(p);
            if (bIdx >= 0 && oversEach[bIdx] > 0) Bowl(line, p, oversEach[bIdx] * 6, format);
            Field(line, p);
            total += line.Runs;
            lines.Add(line);
        }
        return total;
    }

    private void Individual(Profile p, Competition comp, int year, int count, MatchFormat format, string[] opponents, string[] grounds, List<Match> matches, List<PlayerMatchStatistic> lines)
    {
        var team = format is MatchFormat.T20I or MatchFormat.ODI or MatchFormat.Test ? CountryName(p) : p.Player.IsOverseas ? "Demo Overseas Club" : rng.Pick(["Demo State XI A", "Demo State XI B", "Demo State XI C", "Demo State XI D"]);
        for (var i = 0; i < count; i++)
        {
            var opp = rng.Pick(opponents);
            var home = rng.Chance(0.5);
            var m = new Match
            {
                CompetitionId = comp.Id, Year = year, Date = new DateOnly(year, rng.Int(1, 12), rng.Int(1, 28)), Venue = rng.Pick(grounds),
                Team1 = home ? team : opp, Team2 = home ? opp : team, DataSource = Source, IsDemo = true,
            };
            matches.Add(m);
            var line = NewLine(m, p, team, opp, home);
            var position = p.Role switch { "batter" => rng.Int(1, 5), "wk_batter" => rng.Int(1, 7), "allrounder" => rng.Int(4, 8), _ => rng.Int(8, 11) };
            if (position <= 7 || rng.Chance(0.5)) Bat(line, p, position, format);
            if (p.CanBowl && (p.Role is "bowler" or "allrounder" || rng.Chance(0.2)))
            {
                var balls = format switch { MatchFormat.ListA or MatchFormat.ODI => rng.Int(5, 10) * 6, MatchFormat.FirstClass or MatchFormat.Test => rng.Int(12, 40) * 6, _ => rng.Int(2, 4) * 6 };
                Bowl(line, p, balls, format);
            }
            Field(line, p);
            lines.Add(line);
        }
    }

    private PlayerMatchStatistic NewLine(Match m, Profile p, string team, string opponent, bool home) => new()
    {
        MatchId = m.Id, PlayerId = p.Player.Id, Team = team, Opponent = opponent, Venue = m.Venue, IsHome = home,
    };

    private void Bat(PlayerMatchStatistic l, Profile p, int position, MatchFormat format)
    {
        var (meanMul, srMul) = format switch
        {
            MatchFormat.ListA or MatchFormat.ODI => (1.55, 0.72),
            MatchFormat.FirstClass or MatchFormat.Test => (1.9, 0.48),
            _ => (1.0, 1.0),
        };
        var mean = (p.Role switch { "bowler" => 4 + 8 * p.Bat, "allrounder" => 10 + 20 * p.Bat, _ => 14 + 26 * p.Bat }) * meanMul;
        var sr = (108 + 48 * p.Bat + (position >= 5 ? 10 : 0)) * srMul;
        var runs = (int)Math.Min(format is MatchFormat.T20 or MatchFormat.T20I ? 125 : 210, Math.Floor(mean * -Math.Log(1 - rng.Double(0, 0.999))));
        var balls = runs == 0 ? rng.Int(1, 6) : Math.Max(1, (int)Math.Round(runs * 100 / sr * rng.Double(0.85, 1.15)));
        if (format is MatchFormat.T20 or MatchFormat.T20I) balls = Math.Min(balls, 70);
        l.Batted = true;
        l.BattingPosition = position;
        l.Runs = runs;
        l.BallsFaced = balls;
        l.Sixes = Math.Min(runs / 6, (int)Math.Round(runs * 0.055 * (sr / 135) * rng.Double(0.5, 1.5)));
        l.Fours = Math.Min((runs - l.Sixes * 6) / 4, (int)Math.Round(runs * 0.1 * rng.Double(0.6, 1.4)));
        l.Dismissed = rng.Chance(0.82);
        if (l.Dismissed)
        {
            l.DismissalType = rng.Weighted([("Caught", 60), ("Bowled", 15), ("LBW", 12), ("Run out", 8), ("Stumped", 5)]);
            l.DismissedByBowlingType = rng.Chance(0.6) ? "Pace" : "Spin";
        }
        if (format is MatchFormat.T20 or MatchFormat.T20I)
        {
            var (pp, mid) = position switch { <= 2 => (0.55, 0.35), <= 4 => (0.25, 0.55), <= 7 => (0.05, 0.5), _ => (0.0, 0.3) };
            l.PowerplayBalls = (int)Math.Round(balls * pp);
            l.MiddleBalls = (int)Math.Round(balls * mid);
            l.DeathBalls = balls - l.PowerplayBalls - l.MiddleBalls;
            var weights = l.PowerplayBalls * 1.05 + l.MiddleBalls * 0.85 + l.DeathBalls * 1.35;
            l.PowerplayRuns = weights == 0 ? 0 : (int)Math.Round(runs * l.PowerplayBalls * 1.05 / weights);
            l.MiddleRuns = weights == 0 ? 0 : (int)Math.Round(runs * l.MiddleBalls * 0.85 / weights);
            l.DeathRuns = runs - l.PowerplayRuns - l.MiddleRuns;
            if (l.DeathRuns < 0) { l.MiddleRuns += l.DeathRuns; l.DeathRuns = 0; }
        }
        l.BallsVsPace = (int)Math.Round(balls * rng.Double(0.5, 0.7));
        l.BallsVsSpin = balls - l.BallsVsPace;
        var spinSkill = rng.Double(0.8, 1.15);
        var w = l.BallsVsPace + l.BallsVsSpin * spinSkill;
        l.RunsVsPace = w == 0 ? 0 : (int)Math.Round(runs * l.BallsVsPace / w);
        l.RunsVsSpin = runs - l.RunsVsPace;
    }

    private void Bowl(PlayerMatchStatistic l, Profile p, int balls, MatchFormat format)
    {
        var baseEcon = format switch { MatchFormat.ListA or MatchFormat.ODI => 7.4, MatchFormat.FirstClass or MatchFormat.Test => 4.6, _ => 10.2 };
        var econ = Math.Max(3, rng.Normal(baseEcon - 2.8 * p.Bowl, 1.1));
        var overs = balls / 6.0;
        var wicketRate = format switch { MatchFormat.FirstClass or MatchFormat.Test => 0.12, MatchFormat.ListA or MatchFormat.ODI => 0.16, _ => 0.2 } * (0.6 + 1.2 * p.Bowl);
        l.Bowled = true;
        l.BallsBowled = balls;
        l.RunsConceded = (int)Math.Round(overs * econ);
        l.Wickets = Math.Min(format is MatchFormat.FirstClass or MatchFormat.Test ? 9 : 6, rng.Poisson(overs * wicketRate));
        l.DotBalls = (int)Math.Round(balls * Math.Clamp(rng.Normal(0.32 + 0.16 * p.Bowl, 0.05), 0.15, 0.6));
        l.Maidens = format is MatchFormat.T20 or MatchFormat.T20I ? (rng.Chance(0.05 * p.Bowl) ? 1 : 0) : rng.Poisson(overs * 0.12);
        var right = 0;
        for (var i = 0; i < l.Wickets; i++) if (rng.Chance(0.7)) right++;
        l.WicketsVsRightHand = right;
        l.WicketsVsLeftHand = l.Wickets - right;
        if (format is MatchFormat.T20 or MatchFormat.T20I)
        {
            var (pp, mid) = p.IsSpinner ? (0.15, 0.7) : (0.4, 0.25);
            l.PowerplayBallsBowled = (int)Math.Round(balls * pp / 6) * 6;
            l.MiddleBallsBowled = Math.Min(balls - l.PowerplayBallsBowled, (int)Math.Round(balls * mid / 6) * 6);
            l.DeathBallsBowled = balls - l.PowerplayBallsBowled - l.MiddleBallsBowled;
            var weight = l.PowerplayBallsBowled * 1.1 + l.MiddleBallsBowled * 0.85 + l.DeathBallsBowled * 1.3;
            l.PowerplayRunsConceded = weight == 0 ? 0 : (int)Math.Round(l.RunsConceded * l.PowerplayBallsBowled * 1.1 / weight);
            l.MiddleRunsConceded = weight == 0 ? 0 : (int)Math.Round(l.RunsConceded * l.MiddleBallsBowled * 0.85 / weight);
            l.DeathRunsConceded = Math.Max(0, l.RunsConceded - l.PowerplayRunsConceded - l.MiddleRunsConceded);
            for (var i = 0; i < l.Wickets; i++)
            {
                var r = rng.Double(0, balls);
                if (r < l.PowerplayBallsBowled) l.PowerplayWickets++;
                else if (r < l.PowerplayBallsBowled + l.MiddleBallsBowled) l.MiddleWickets++;
                else l.DeathWickets++;
            }
        }
    }

    private void Field(PlayerMatchStatistic l, Profile p)
    {
        if (p.Player.IsWicketKeeper && p.Role == "wk_batter")
        {
            l.Catches = rng.Poisson(0.9);
            l.Stumpings = rng.Poisson(0.15);
        }
        else l.Catches = rng.Poisson(0.32);
        l.RunOuts = rng.Chance(0.05) ? 1 : 0;
    }

    private string CountryName(Profile p) => p.Player.IsOverseas
        ? db.Countries.Local.First(c => c.Id == p.Player.CountryId).Name
        : "India";

    private static string RoleName(string code) => code switch { "batter" => "Batter", "wk_batter" => "Wicketkeeper", "allrounder" => "All-rounder", _ => "Bowler" };

    private static string BowlingStyle(BowlingType t, Rng rng) => t switch
    {
        BowlingType.Fast => rng.Chance(0.25) ? "Left-arm fast" : "Right-arm fast",
        BowlingType.MediumFast => rng.Chance(0.25) ? "Left-arm fast-medium" : "Right-arm fast-medium",
        BowlingType.Medium => "Right-arm medium",
        BowlingType.OffSpin => "Right-arm off-break",
        BowlingType.LegSpin => "Right-arm leg-break",
        BowlingType.LeftArmOrthodox => "Slow left-arm orthodox",
        BowlingType.LeftArmWristSpin => "Left-arm wrist-spin",
        _ => "",
    };

    // ------------------------------------------------------------------ bulk insert

    /// <summary>Fast insert of thousands of generated rows with SqlBulkCopy (scalar columns named after properties).</summary>
    /// <summary>Fast insert of new rows with SqlBulkCopy (joins the current transaction, if any).</summary>
    internal static async Task BulkInsertAsync<T>(AppDbContext db, IReadOnlyList<T> rows, string table, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        var entity = db.Model.FindEntityType(typeof(T))!;
        var props = entity.GetProperties().Where(p => p.PropertyInfo is not null).ToList();
        var dt = new DataTable();
        foreach (var p in props)
        {
            var type = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
            dt.Columns.Add(p.GetColumnName(), type == typeof(DateOnly) ? typeof(DateTime) : type);
        }
        foreach (var row in rows)
            dt.Rows.Add(props.Select(p =>
            {
                var v = p.PropertyInfo!.GetValue(row);
                return v switch { null => DBNull.Value, DateOnly d => d.ToDateTime(TimeOnly.MinValue), _ => v };
            }).ToArray());
        var conn = (SqlConnection)db.Database.GetDbConnection();
        var wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);
        try
        {
            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.CheckConstraints, (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction())
            {
                DestinationTableName = $"[ipl].[{table}]", BatchSize = 5000, BulkCopyTimeout = 300,
            };
            foreach (DataColumn c in dt.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await bulk.WriteToServerAsync(dt, ct);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }
}

/// <summary>Deterministic random numbers (same demo data on every machine).</summary>
public sealed class Rng(int seed)
{
    private readonly Random random = new(seed);
    public int Next() => random.Next();
    public int Int(int min, int maxInclusive) => random.Next(min, maxInclusive + 1);
    public double Double(double min, double max) => min + random.NextDouble() * (max - min);
    public bool Chance(double p) => random.NextDouble() < p;
    public T Pick<T>(IReadOnlyList<T> items) => items[random.Next(items.Count)];
    public double Normal(double mean, double sd)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return mean + sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
    public int Poisson(double lambda)
    {
        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do { k++; p *= random.NextDouble(); } while (p > l);
        return k - 1;
    }
    public string Weighted(IReadOnlyList<(string Value, int Weight)> items)
    {
        var r = random.Next(items.Sum(i => i.Weight));
        foreach (var (value, weight) in items) { if (r < weight) return value; r -= weight; }
        return items[^1].Value;
    }
}

/// <summary>Invented names. Combinations are random, so any resemblance to a real person is coincidental.</summary>
public static class Names
{
    private static readonly string[] IndianFirst =
        ["Aarav", "Vihaan", "Advik", "Reyansh", "Ishaan", "Arnav", "Dhruv", "Ayaan", "Vivaan", "Shaurya", "Atharv", "Rudra", "Neel", "Pranav", "Siddhant",
         "Tanmay", "Omkar", "Harshit", "Nikhil", "Sameer", "Kunal", "Raghav", "Manav", "Lakshya", "Parth", "Ritvik", "Samar", "Tejas", "Uday", "Veer",
         "Zaid", "Farhan", "Anirudh", "Devansh", "Eshan", "Gaurang", "Hridaan", "Jivin", "Kairav", "Mihir", "Naman", "Ojas", "Pratyush", "Rishabh", "Saransh",
         "Trilok", "Utkarsh", "Vedant", "Yuvan", "Aniket", "Chirag", "Darsh", "Hemant", "Indranil", "Jatin", "Keshav", "Mayank", "Nishant", "Prithvi", "Shlok"];
    private static readonly string[] IndianLast =
        ["Mehra", "Kulkarni", "Deshmukh", "Rathore", "Nair", "Menon", "Iyer", "Bhatia", "Chauhan", "Saxena", "Joshi", "Malhotra", "Bose", "Sen", "Naidu",
         "Pillai", "Sandhu", "Grewal", "Bajwa", "Dubey", "Tripathi", "Banerjee", "Chatterjee", "Kamath", "Shetty", "Hegde", "Bhosale", "Gaikwad", "Jadhav",
         "Pawar", "Rawat", "Negi", "Bisht", "Kashyap", "Ahuja", "Arora", "Khanna", "Sethi", "Talwar", "Venkatesan", "Raman", "Krishnan", "Varma", "Bhalla",
         "Dhillon", "Sood", "Kohar", "Mathur", "Nagpal", "Oberoi"];
    private static readonly Dictionary<string, (string[] First, string[] Last)> OverseasNames = new()
    {
        ["AUS"] = (["Lachlan", "Callum", "Riley", "Hamish", "Brodie", "Fletcher", "Jarrah", "Cooper", "Darcy", "Tate"], ["Thornbury", "Whitcombe", "Ashdale", "Pemberly", "Langridge", "Calloway", "Fairweather", "Kingsley", "Ridgeway", "Blackwood"]),
        ["ENG"] = (["Oliver", "Harry", "Alfie", "Freddie", "Archie", "Theo", "Rupert", "Hugo", "Felix", "Jasper"], ["Hargreaves", "Whitaker", "Ashford", "Pennington", "Lockwood", "Thistlewood", "Marchbanks", "Ellery", "Southgate-Ross", "Bramley"]),
        ["RSA"] = (["Pieter", "Johan", "Ruan", "Thabo", "Sipho", "Wian", "Lungi-Mpho", "Kagiso-Leo", "Dewald", "Jaco"], ["van Wyk", "Mokoena", "Naidoo", "Coetzer", "Botman", "Dlamini", "Swanepoel", "Ferreira", "Pretorius-Kok", "Mabaso"]),
        ["NZL"] = (["Finn", "Hunter", "Nikau", "Arlo", "Beau", "Kahu", "Ollie", "Reuben", "Tama", "Zane"], ["Harcourt", "Te Rangi", "McAllister", "Penrose", "Whitfield", "Kerrigan", "Ashburton", "Rutherford-Lee", "Tamati", "Galbraith"]),
        ["WIN"] = (["Jermaine", "Akeem", "Rashawn", "Tevin", "Delano", "Kemar", "Javier", "Shamar", "Orlando", "Nkosi"], ["Bascombe", "Gittens", "Thorne", "Mayers-Hunte", "Clarkson", "Belgrave", "Cumberbatch", "Wickham", "Alleyne", "Quashie"]),
        ["AFG"] = (["Farid", "Naveed", "Hamid", "Sohail", "Wahid", "Bilal", "Zubair", "Jawed", "Mirwais", "Sami"], ["Karimi", "Sadat", "Nazari", "Amiri", "Rahmani", "Hakimi", "Jalali", "Popal", "Wardak", "Azimi"]),
        ["SRI"] = (["Kasun", "Lahiru", "Tharindu", "Isuru", "Chamath", "Nuwan", "Dilshan-Ravi", "Sahan", "Pasindu", "Ravindu"], ["Wijesinghe", "Gunasekara", "Rathnayake", "Abeysekera", "Jayalath", "Karunaratne-De", "Samarasinghe", "Weerakoon", "Ekanayake", "Ranaweera"]),
        ["BAN"] = (["Arif", "Tanvir", "Rakibul", "Sabbir-Khan", "Mehedi-Ali", "Shoriful-Amin", "Nayeem", "Rezaul", "Saif", "Tawhid"], ["Chowdhury-Rana", "Hossain-Mia", "Sarkar", "Talukder", "Bhuiyan", "Molla", "Kabir", "Siddique", "Mridha", "Akanda"]),
        ["IRE"] = (["Cian", "Oisin", "Darragh", "Fionn", "Ronan"], ["O'Driscoll", "Gallagher", "McCarthy-Byrne", "Fitzgerald", "Mulcahy"]),
        ["NED"] = (["Daan", "Sem", "Bram", "Jesse", "Thijs"], ["van Dijkstra", "de Groot", "Vermeulen", "Bakker-Jansen", "Hendriks"]),
    };

    public static string Indian(Rng rng) => $"{rng.Pick(IndianFirst)} {rng.Pick(IndianLast)}";

    public static string Overseas(Rng rng, string countryCode)
    {
        var (first, last) = OverseasNames.TryGetValue(countryCode, out var n) ? n : OverseasNames["ENG"];
        return $"{rng.Pick(first)} {rng.Pick(last)}";
    }
}

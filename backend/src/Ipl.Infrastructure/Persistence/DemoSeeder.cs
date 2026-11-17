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
    public const string Source = "Demo generator (synthetic) — not real statistics";
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
        log.LogInformation("Seeding the fictional demo league…");
        var roles = await db.PlayerRoles.ToDictionaryAsync(r => r.Code, ct);
        var countries = await db.Countries.ToDictionaryAsync(c => c.Code, ct);

        // ---------------- Competitions & seasons ----------------
        Competition C(string code, string name, CompetitionType t, MatchFormat f, int level) => new() { Code = code, Name = name, Type = t, Format = f, Level = level, IsDemo = true };
        var dpl = C("DPL", "Demo Premier League", CompetitionType.Franchise, MatchFormat.T20, 5);
        var otl = C("DOTL", "Demo Overseas T20 League", CompetitionType.Franchise, MatchFormat.T20, 4);
        var dt20 = C("DT20", "Demo Domestic T20 Trophy", CompetitionType.Domestic, MatchFormat.T20, 3);
        var dod = C("DODC", "Demo Domestic One-Day Cup", CompetitionType.Domestic, MatchFormat.ListA, 3);
        var dfc = C("DFCC", "Demo First-Class Championship", CompetitionType.Domestic, MatchFormat.FirstClass, 3);
        var t20i = C("DT20I", "Demo T20 Internationals", CompetitionType.International, MatchFormat.T20I, 5);
        var odi = C("DODI", "Demo One-Day Internationals", CompetitionType.International, MatchFormat.ODI, 5);
        var test = C("DTEST", "Demo Test Matches", CompetitionType.International, MatchFormat.Test, 5);
        db.Competitions.AddRange(dpl, otl, dt20, dod, dfc, t20i, odi, test);

        var currentYear = Today.Year + (Today.Month >= 6 ? 1 : 0);
        var historic = Enumerable.Range(currentYear - Math.Clamp(options.HistoricSeasons, 1, 5), Math.Clamp(options.HistoricSeasons, 1, 5)).ToList();
        var seasons = new Dictionary<int, Season>();
        foreach (var y in historic.Append(currentYear))
        {
            var s = new Season
            {
                Year = y, Name = $"DPL {y}", StartDate = new DateOnly(y, 3, 22), EndDate = new DateOnly(y, 5, 28), IsCurrent = y == currentYear,
                Status = y == currentYear ? SeasonStatus.Auction : SeasonStatus.Completed,
            };
            seasons[y] = s;
            db.Seasons.Add(s);
        }
        await db.SaveChangesAsync(ct);
        foreach (var (y, s) in seasons) AddRules(s, y == currentYear);

        // ---------------- Franchises ----------------
        (string Code, string Name, string Short, string City, string Ground, string Owner, string Company, string P, string S)[] fr =
        [
            ("MUM", "Mumbai Mariners", "Mariners", "Mumbai", "Marine Park Stadium", "Anaya Kulkarni", "Harbourline Ventures", "#1d4ed8", "#facc15"),
            ("CHN", "Chennai Cheetahs", "Cheetahs", "Chennai", "Bay Breeze Arena", "Ravi Subramanian", "Coromandel Holdings", "#f59e0b", "#1e3a8a"),
            ("BLR", "Bengaluru Blazers", "Blazers", "Bengaluru", "Garden City Oval", "Meera Hegde", "Silicon Plateau Group", "#dc2626", "#111827"),
            ("KOL", "Kolkata Comets", "Comets", "Kolkata", "Hooghly Riverside Ground", "Arindam Bose", "Eastern Lights Pvt Ltd", "#7c3aed", "#fbbf24"),
            ("DEL", "Delhi Defenders", "Defenders", "Delhi", "Yamuna Capital Stadium", "Kabir Malhotra", "Capital Crest Ltd", "#2563eb", "#ef4444"),
            ("HYD", "Hyderabad Hawks", "Hawks", "Hyderabad", "Deccan Heights Stadium", "Lakshmi Reddy", "Charminar Capital", "#ea580c", "#0f172a"),
            ("JAI", "Jaipur Jaguars", "Jaguars", "Jaipur", "Pink City Cricket Ground", "Vikram Rathore", "Aravalli Industries", "#db2777", "#1e40af"),
            ("PUN", "Punjab Panthers", "Panthers", "Mohali", "Five Rivers Stadium", "Harleen Grewal", "Golden Fields Agro", "#b91c1c", "#d1d5db"),
            ("LKO", "Lucknow Lancers", "Lancers", "Lucknow", "Nawab Park Stadium", "Siddharth Tripathi", "Awadh Enterprises", "#0891b2", "#f97316"),
            ("AHM", "Ahmedabad Arrows", "Arrows", "Ahmedabad", "Sabarmati Riverfront Ground", "Nirali Mehta", "Gujarat Kite Holdings", "#0f766e", "#eab308"),
        ];
        var franchises = new List<Franchise>();
        foreach (var f in fr)
        {
            var franchise = new Franchise
            {
                Code = f.Code, Name = f.Name, ShortName = f.Short, City = f.City, HomeGround = f.Ground, OwnerName = f.Owner, OwnerCompany = f.Company,
                OwnerEmail = $"owner.{f.Code.ToLowerInvariant()}@demo.local", PrimaryColor = f.P, SecondaryColor = f.S, FoundedYear = historic.Min() - rng.Int(0, 8),
                IsDemo = true, CreatedAt = now,
            };
            franchise.Staff.AddRange([
                new FranchiseStaff { Name = f.Owner, Position = "Owner", Category = StaffCategory.Management },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Chief Executive Officer", Category = StaffCategory.Management, SinceYear = historic.Min() },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Cricket Director", Category = StaffCategory.Management, SinceYear = historic[^1] },
                new FranchiseStaff { Name = Names.Overseas(rng, "AUS"), Position = "Head Coach", Category = StaffCategory.Coaching, SinceYear = historic[Math.Max(0, historic.Count - 2)] },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Batting Coach", Category = StaffCategory.Coaching, SinceYear = historic[^1] },
                new FranchiseStaff { Name = Names.Overseas(rng, "RSA"), Position = "Bowling Coach", Category = StaffCategory.Coaching, SinceYear = historic[^1] },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Fielding Coach", Category = StaffCategory.Coaching, SinceYear = historic.Min() },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Lead Physiotherapist", Category = StaffCategory.Support, SinceYear = historic.Min() },
                new FranchiseStaff { Name = Names.Indian(rng), Position = "Performance Analyst", Category = StaffCategory.Support, SinceYear = historic[^1] },
            ]);
            franchises.Add(franchise);
            db.Franchises.Add(franchise);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Players ----------------
        var profiles = new List<Profile>();
        var codeNo = 0;
        Profile NewPlayer(string role, bool overseas, bool keeper, Franchise? franchise, string? countryCode = null)
        {
            var cc = overseas ? countryCode ?? rng.Pick(["AUS", "ENG", "RSA", "NZL", "WIN", "AFG", "SRI", "BAN", "IRE", "NED"]) : "IND";
            var age = rng.Int(19, 37);
            var dob = new DateOnly(Today.Year - age, rng.Int(1, 12), rng.Int(1, 28));
            var hand = rng.Chance(0.3) ? BattingHand.LeftHand : BattingHand.RightHand;
            BowlingType bt;
            if (role == "bowler" || role == "allrounder")
                bt = rng.Pick([BowlingType.Fast, BowlingType.Fast, BowlingType.MediumFast, BowlingType.MediumFast, BowlingType.OffSpin, BowlingType.LegSpin, BowlingType.LeftArmOrthodox, BowlingType.LeftArmWristSpin]);
            else bt = rng.Chance(0.25) ? rng.Pick([BowlingType.OffSpin, BowlingType.Medium]) : BowlingType.None;
            var spinner = bt is BowlingType.OffSpin or BowlingType.LegSpin or BowlingType.LeftArmOrthodox or BowlingType.LeftArmWristSpin;
            var name = overseas ? Names.Overseas(rng, cc) : Names.Indian(rng);
            var talent = Math.Clamp(rng.Normal(0.5, 0.18), 0.05, 0.98);
            var p = new Player
            {
                Code = $"P{++codeNo:00000}", FullName = name, DisplayName = name, DateOfBirth = dob, CountryId = countries[cc].Id, IsOverseas = overseas,
                BattingHand = hand, BowlingType = bt, BowlingStyle = BowlingStyle(bt, rng), PrimaryRoleId = roles[role].Id,
                IsWicketKeeper = keeper || role == "wk_batter", IsCapped = overseas ? rng.Chance(0.8) : rng.Chance(age > 24 ? 0.4 * talent + 0.15 : 0.05),
                Status = franchise is null ? PlayerStatus.AvailableForAuction : PlayerStatus.UnderContract, CareerStatus = CareerStatus.Active,
                CurrentFranchiseId = franchise?.Id, IsDemo = true, DataSource = Source, CreatedAt = now,
            };
            if (role == "allrounder") p.SecondaryRoleId = roles[spinner || rng.Chance(0.5) ? "bowler" : "batter"].Id;
            var profile = new Profile
            {
                Player = p, Role = role, Franchise = franchise, CanBowl = bt != BowlingType.None && role != "wk_batter", IsSpinner = spinner,
                Bat = role switch { "bowler" => talent * 0.35, "allrounder" => talent * 0.8, _ => talent },
                Bowl = role switch { "bowler" => talent, "allrounder" => talent * 0.85, _ => talent * 0.3 },
            };
            profiles.Add(profile);
            db.Players.Add(p);
            return profile;
        }

        foreach (var f in franchises)
        {
            string[] plan = ["batter", "batter", "batter", "batter", "batter", "wk_batter", "wk_batter", "allrounder", "allrounder", "allrounder", "allrounder",
                "allrounder", "bowler", "bowler", "bowler", "bowler", "bowler", "bowler", "bowler", "batter"];
            var overseasSlots = new HashSet<int>(Enumerable.Range(0, plan.Length).OrderBy(_ => rng.Next()).Take(7));
            for (var i = 0; i < plan.Length; i++) NewPlayer(plan[i], overseasSlots.Contains(i), false, f);
        }
        for (var i = 0; i < 70; i++)
        {
            var role = rng.Pick(["batter", "batter", "wk_batter", "allrounder", "allrounder", "bowler", "bowler", "bowler"]);
            NewPlayer(role, rng.Chance(0.35), false, null);
        }

        // Prices: a fictional previous auction. Squads stay inside the purse with room left for the coming auction.
        var purse = 120 * Money.Crore;
        foreach (var group in profiles.Where(p => p.Franchise is not null).GroupBy(p => p.Franchise!))
        {
            foreach (var p in group)
            {
                var talent = Math.Max(p.Bat, p.Bowl);
                var raw = (decimal)Math.Pow(talent, 2.4) * 16 * Money.Crore * (p.Player.IsCapped ? 1 : 0.35m);
                p.Price = Math.Max(20 * Money.Lakh, Math.Round(raw / (5 * Money.Lakh)) * 5 * Money.Lakh);
            }
            var total = group.Sum(p => p.Price);
            var target = (decimal)rng.Double(88, 104) * Money.Crore;
            if (total > target)
                foreach (var p in group) p.Price = Math.Max(20 * Money.Lakh, Math.Round(p.Price * target / total / (5 * Money.Lakh)) * 5 * Money.Lakh);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- History: who played where ----------------
        foreach (var p in profiles)
        {
            var age = Today.Year - p.Player.DateOfBirth.Year;
            var firstPossible = Math.Max(historic.Min(), Today.Year - (age - 19));
            p.DplDebut = rng.Chance(0.85) ? rng.Int(Math.Min(firstPossible, historic.Max()), historic.Max()) : int.MaxValue;
            if (p.Franchise is null && rng.Chance(0.4)) p.DplDebut = int.MaxValue;
            var former = rng.Pick(franchises);
            var switchYear = rng.Chance(0.25) ? rng.Int(historic.Min(), historic.Max()) : historic.Min();
            foreach (var y in historic.Where(y => y >= p.DplDebut))
            {
                var team = p.Franchise is null ? former : y < switchYear ? former : p.Franchise;
                p.History[y] = team;
                db.PlayerTeams.Add(new PlayerTeam
                {
                    PlayerId = p.Player.Id, FranchiseId = team.Id, SeasonId = seasons[y].Id,
                    Acquisition = y == p.DplDebut ? AcquisitionType.Auction : AcquisitionType.Retained, Price = p.Price > 0 ? p.Price : 30 * Money.Lakh,
                    FromDate = seasons[y].StartDate, ToDate = seasons[y].EndDate,
                });
            }
        }

        // ---------------- Simulated matches ----------------
        var matches = new List<Match>();
        var lines = new List<PlayerMatchStatistic>();
        var tables = new Dictionary<(int Year, Guid Fid), (int P, int W, int L, int NR, int Pts, decimal For, decimal Against)>();
        foreach (var y in historic)
        {
            foreach (var f in franchises) tables[(y, f.Id)] = (0, 0, 0, 0, 0, 0, 0);
            var fixtures = franchises.SelectMany(a => franchises.Where(b => b != a).Select(b => (Home: a, Away: b))).ToList();
            var date = seasons[y].StartDate;
            var i = 0;
            foreach (var (home, away) in fixtures)
            {
                var m = new Match { CompetitionId = dpl.Id, Year = y, Date = date.AddDays(i++ * 60 / fixtures.Count), Venue = home.HomeGround, Team1 = home.Name, Team2 = away.Name, DataSource = Source, IsDemo = true };
                var homeXI = PickXI(profiles.Where(p => p.History.TryGetValue(y, out var t) && t == home).ToList());
                var awayXI = PickXI(profiles.Where(p => p.History.TryGetValue(y, out var t) && t == away).ToList());
                var homeRuns = SimulateTeam(m, homeXI, home.Name, away.Name, true, MatchFormat.T20, lines);
                var awayRuns = SimulateTeam(m, awayXI, away.Name, home.Name, false, MatchFormat.T20, lines);
                var homeWon = homeRuns == awayRuns ? rng.Chance(0.5) : homeRuns > awayRuns;
                m.Result = $"{(homeWon ? home.Name : away.Name)} won ({homeRuns} v {awayRuns})";
                matches.Add(m);
                var h = tables[(y, home.Id)];
                var a = tables[(y, away.Id)];
                tables[(y, home.Id)] = (h.P + 1, h.W + (homeWon ? 1 : 0), h.L + (homeWon ? 0 : 1), h.NR, h.Pts + (homeWon ? 2 : 0), h.For + homeRuns, h.Against + awayRuns);
                tables[(y, away.Id)] = (a.P + 1, a.W + (homeWon ? 0 : 1), a.L + (homeWon ? 1 : 0), a.NR, a.Pts + (homeWon ? 0 : 2), a.For + awayRuns, a.Against + homeRuns);
            }
            var ranked = franchises.OrderByDescending(f => tables[(y, f.Id)].Pts).ThenByDescending(f => tables[(y, f.Id)].For - tables[(y, f.Id)].Against).ToList();
            var championIndex = rng.Chance(0.6) ? 0 : 1;
            for (var pos = 0; pos < ranked.Count; pos++)
            {
                var t = tables[(y, ranked[pos].Id)];
                db.FranchiseSeasons.Add(new FranchiseSeason
                {
                    FranchiseId = ranked[pos].Id, SeasonId = seasons[y].Id, PurseTotal = purse, PurseRemaining = (decimal)rng.Double(0.5, 6) * Money.Crore,
                    Played = t.P, Won = t.W, Lost = t.L, NoResult = t.NR, Points = t.Pts, NetRunRate = Math.Round((t.For - t.Against) / (t.P * 20m), 3),
                    FinalPosition = pos + 1 == 1 ? (championIndex == 0 ? 1 : 2) : pos + 1 == 2 ? (championIndex == 0 ? 2 : 1) : pos + 1,
                    IsChampion = pos == championIndex, IsRunnerUp = pos == 1 - championIndex,
                });
            }
        }
        // Domestic, overseas league and international matches (individual player records).
        string[] domesticTeams = ["Northern Stars XI", "Coastal Kings XI", "Central Rangers XI", "Eastern Tigers XI", "Western Sharks XI", "Southern Suns XI", "Highland Hawks XI", "Desert Foxes XI"];
        string[] grounds = ["Demo Ground 1", "Demo Ground 2", "Demo Ground 3", "Demo Ground 4", "Demo Ground 5", "Demo Ground 6"];
        foreach (var p in profiles)
            foreach (var y in historic)
            {
                var age = y - p.Player.DateOfBirth.Year;
                if (age < 18) continue;
                if (!p.Player.IsOverseas)
                {
                    Individual(p, dt20, y, rng.Int(4, 8), MatchFormat.T20, domesticTeams, grounds, matches, lines);
                    Individual(p, dod, y, rng.Int(3, 6), MatchFormat.ListA, domesticTeams, grounds, matches, lines);
                    if (p.Role != "wk_batter" || rng.Chance(0.6)) Individual(p, dfc, y, rng.Int(2, 5), MatchFormat.FirstClass, domesticTeams, grounds, matches, lines);
                }
                else Individual(p, otl, y, rng.Int(6, 10), MatchFormat.T20, ["Demo Harbour XI", "Demo Plains XI", "Demo Valley XI", "Demo Lakes XI"], grounds, matches, lines);
                if (p.Player.IsCapped && age >= 21 && rng.Chance(0.75))
                {
                    var opponents = new[] { "India", "Australia", "England", "South Africa", "New Zealand", "West Indies", "Sri Lanka", "Afghanistan" }
                        .Where(o => o != CountryName(p)).ToArray();
                    Individual(p, t20i, y, rng.Int(2, 7), MatchFormat.T20I, opponents, grounds, matches, lines);
                    if (p.Role != "bowler" || rng.Chance(0.7)) Individual(p, odi, y, rng.Int(1, 5), MatchFormat.ODI, opponents, grounds, matches, lines);
                    if (Math.Max(p.Bat, p.Bowl) > 0.55 && rng.Chance(0.5)) Individual(p, test, y, rng.Int(1, 4), MatchFormat.Test, opponents, grounds, matches, lines);
                }
            }

        await db.SaveChangesAsync(ct);
        await BulkInsertAsync(matches, "Matches", ct);
        await BulkInsertAsync(lines, "PlayerMatchStatistics", ct);

        // Season and career totals computed from the match rows.
        var compByMatch = matches.ToDictionary(m => m.Id, m => (m.CompetitionId, m.Year));
        var seasonRows = new List<PlayerSeasonStatistic>();
        foreach (var g in lines.GroupBy(l => (l.PlayerId, compByMatch[l.MatchId].CompetitionId, compByMatch[l.MatchId].Year)))
        {
            var row = new PlayerSeasonStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId, Year = g.Key.Year, DataSource = Source, LastUpdatedAt = now };
            StatisticsService.Aggregate(row, g.ToList());
            row.TeamName = g.First().Team;
            seasonRows.Add(row);
        }
        db.PlayerSeasonStatistics.AddRange(seasonRows);
        foreach (var g in seasonRows.GroupBy(s => (s.PlayerId, s.CompetitionId)))
        {
            var career = new PlayerCareerStatistic { PlayerId = g.Key.PlayerId, CompetitionId = g.Key.CompetitionId, FirstYear = g.Min(s => s.Year), LastYear = g.Max(s => s.Year), DataSource = Source, LastUpdatedAt = now };
            StatisticsService.Sum(career, g.ToList());
            db.PlayerCareerStatistics.Add(career);
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Current season: squads, contracts, purses, finance ----------------
        var current = seasons[currentYear];
        foreach (var f in franchises)
        {
            var squad = profiles.Where(p => p.Franchise == f).ToList();
            var spent = squad.Sum(p => p.Price);
            db.FranchiseSeasons.Add(new FranchiseSeason { FranchiseId = f.Id, SeasonId = current.Id, PurseTotal = purse, PurseRemaining = purse - spent, OperatingBudget = 45 * Money.Crore });
            db.FinanceTransactions.Add(new FinanceTransaction
            {
                FranchiseId = f.Id, SeasonId = current.Id, Category = FinanceCategory.AuctionPurse, Direction = FinanceDirection.Credit, Amount = purse,
                PurseBalanceAfter = purse, Description = $"{current.Name} auction purse allocation", IdempotencyKey = $"purse-alloc:{f.Id}:{current.Id}", CreatedAt = now.AddDays(-60),
            });
            var balance = purse;
            var k = 0;
            foreach (var p in squad.OrderByDescending(p => p.Price))
            {
                balance -= p.Price;
                db.FinanceTransactions.Add(new FinanceTransaction
                {
                    FranchiseId = f.Id, SeasonId = current.Id, Category = FinanceCategory.AuctionPurse, Direction = FinanceDirection.Debit, Amount = p.Price,
                    PurseBalanceAfter = balance, Description = $"Squad carried forward: {p.Player.FullName}", ReferenceType = "Player", ReferenceId = p.Player.Id.ToString(),
                    IdempotencyKey = $"carry:{current.Id}:{p.Player.Id}", CreatedAt = now.AddDays(-59).AddMinutes(k++),
                });
                db.FranchiseSquads.Add(new FranchiseSquad
                {
                    FranchiseId = f.Id, SeasonId = current.Id, PlayerId = p.Player.Id, Acquisition = p.History.ContainsKey(historic.Max()) ? AcquisitionType.Retained : AcquisitionType.Auction,
                    Price = p.Price, Status = SquadMemberStatus.Active, JoinedAt = now.AddDays(-59),
                });
                var contract = new PlayerContract
                {
                    PlayerId = p.Player.Id, FranchiseId = f.Id, StartSeasonId = current.Id, EndSeasonId = current.Id, StartDate = current.StartDate.AddDays(-30),
                    EndDate = current.EndDate, AnnualSalary = p.Price, Acquisition = AcquisitionType.Auction, Status = ContractStatus.Active, CreatedAt = now.AddDays(-59),
                };
                for (var n = 0; n < 3; n++)
                    contract.Payments.Add(new ContractPayment { DueDate = contract.StartDate.AddDays(n * 30), Amount = Math.Round(p.Price / 3, 2), Status = PaymentStatus.Scheduled });
                contract.Payments[2].Amount = p.Price - contract.Payments[0].Amount * 2;
                db.PlayerContracts.Add(contract);
                db.PlayerTeams.Add(new PlayerTeam { PlayerId = p.Player.Id, FranchiseId = f.Id, SeasonId = current.Id, Acquisition = AcquisitionType.Retained, Price = p.Price, FromDate = current.StartDate });
            }
        }
        // A few availability issues (public notes only — no medical detail).
        foreach (var p in profiles.Where(p => p.Franchise is not null).OrderBy(_ => rng.Next()).Take(9))
        {
            var status = rng.Pick([AvailabilityStatus.Injured, AvailabilityStatus.Doubtful, AvailabilityStatus.NationalDuty]);
            db.PlayerAvailability.Add(new PlayerAvailability { PlayerId = p.Player.Id, Status = status, PublicNote = status == AvailabilityStatus.NationalDuty ? "On national duty" : "Under assessment", ExpectedReturn = Today.AddDays(rng.Int(10, 60)), UpdatedAt = now });
            if (status == AvailabilityStatus.Injured) p.Player.Status = PlayerStatus.Injured;
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Users ----------------
        var roleIds = await db.Roles.ToDictionaryAsync(r => r.Code, r => r.Id, ct);
        User NewUser(string email, string name, string? role, Guid? franchiseId, string? title = null)
        {
            var u = new User
            {
                Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = name, IsActive = true, IsActivated = true, DefaultFranchiseId = franchiseId, CreatedAt = now,
            };
            u.PasswordHash = hasher.HashPassword(u, options.Password);
            db.Users.Add(u);
            if (role is not null) db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = roleIds[role], FranchiseId = franchiseId, AssignedAt = now });
            if (franchiseId is { } fid) db.FranchiseMembers.Add(new FranchiseMember { FranchiseId = fid, UserId = u.Id, JobTitle = title ?? "", JoinedAt = now });
            return u;
        }
        NewUser("admin@demo.local", "Platform Administrator", RoleCatalog.SuperAdmin, null);
        var league = NewUser("league@demo.local", "League Operations", RoleCatalog.LeagueAdmin, null);
        var mum = franchises[0];
        var chn = franchises[1];
        var users = new Dictionary<string, User>();
        foreach (var (code, name, _, _, _) in RoleCatalog.All.Where(r => r.Scope == RoleScope.Franchise))
        {
            var u = NewUser($"{code.Replace("franchise_", "").Replace("_", ".")}.mum@demo.local", $"{Names.Indian(rng)}", code, mum.Id, name);
            users[code] = u;
        }
        var scout2 = NewUser("scout2.mum@demo.local", Names.Indian(rng), RoleCatalog.Scout, mum.Id, "Scout");
        foreach (var code in new[] { RoleCatalog.Owner, RoleCatalog.Ceo, RoleCatalog.CricketDirector, RoleCatalog.AuctionManager, RoleCatalog.Operations })
            NewUser($"{code.Replace("franchise_", "").Replace("_", ".")}.chn@demo.local", Names.Indian(rng), code, chn.Id, RoleCatalog.All.First(r => r.Code == code).Name);
        foreach (var f in franchises.Skip(2))
        {
            NewUser($"owner.{f.Code.ToLowerInvariant()}@demo.local", f.OwnerName, RoleCatalog.Owner, f.Id, "Owner");
            NewUser($"auction.{f.Code.ToLowerInvariant()}@demo.local", Names.Indian(rng), RoleCatalog.AuctionManager, f.Id, "Auction Manager");
        }
        await db.SaveChangesAsync(ct);

        // ---------------- Auction pool ----------------
        var auction = new AuctionEvent
        {
            SeasonId = current.Id, Name = $"DPL {currentYear} Player Auction", IsMegaAuction = false, ScheduledAt = now.Date.AddDays(14).AddHours(9),
            Status = AuctionStatus.Ready, BidTimerSeconds = 30, AllowReopenUnsold = true, CreatedAt = now, CreatedBy = league.Id,
        };
        db.AuctionEvents.Add(auction);
        var lot = 0;
        var setNo = 0;
        foreach (var g in profiles.Where(p => p.Franchise is null).GroupBy(p => (p.Player.IsCapped, p.Role)).OrderByDescending(g => g.Key.IsCapped))
        {
            setNo++;
            foreach (var p in g.OrderByDescending(p => Math.Max(p.Bat, p.Bowl)))
            {
                var t = Math.Max(p.Bat, p.Bowl);
                var basePrice = !p.Player.IsCapped ? 20 * Money.Lakh : t > 0.7 ? 2 * Money.Crore : t > 0.55 ? Money.Crore : t > 0.4 ? 50 * Money.Lakh : 30 * Money.Lakh;
                db.AuctionPlayers.Add(new AuctionPlayer
                {
                    AuctionEventId = auction.Id, PlayerId = p.Player.Id, SetName = $"{(g.Key.IsCapped ? "Capped" : "Uncapped")} {RoleName(g.Key.Role)}s", SetNumber = setNo,
                    LotNumber = ++lot, BasePrice = basePrice, Status = AuctionLotStatus.Pending,
                });
            }
        }

        // ---------------- Sample workflow data ----------------
        var mumSquad = profiles.Where(p => p.Franchise == mum).ToList();
        var chnSquad = profiles.Where(p => p.Franchise == chn).ToList();
        var offered = mumSquad.Where(p => p.Role == "bowler" && !p.Player.IsOverseas).OrderBy(p => p.Price).First();
        var requested = chnSquad.Where(p => p.Role == "batter" && !p.Player.IsOverseas).OrderBy(p => p.Price).First();
        var trade = new TradeOffer
        {
            Reference = $"TR-{currentYear}-0001", SeasonId = current.Id, InitiatorFranchiseId = mum.Id, CounterpartyFranchiseId = chn.Id,
            Status = TradeStatus.PendingResponse, CashConsideration = 50 * Money.Lakh, CashPayerFranchiseId = mum.Id, AwaitingFranchiseId = chn.Id,
            SubmittedAt = now.AddDays(-1), ExpiresAt = now.AddDays(6), Conditions = "Subject to medical clearance of both players.", CreatedAt = now.AddDays(-2),
            CreatedBy = users[RoleCatalog.CricketDirector].Id,
        };
        trade.Players.Add(new TradeOfferPlayer { PlayerId = offered.Player.Id, FromFranchiseId = mum.Id, ToFranchiseId = chn.Id, ContractSalary = offered.Price });
        trade.Players.Add(new TradeOfferPlayer { PlayerId = requested.Player.Id, FromFranchiseId = chn.Id, ToFranchiseId = mum.Id, ContractSalary = requested.Price });
        trade.Negotiations.Add(new TradeNegotiation { RevisionNo = 1, Action = NegotiationAction.Created, ByFranchiseId = mum.Id, ByUserId = users[RoleCatalog.CricketDirector].Id, ByUserName = users[RoleCatalog.CricketDirector].FullName, At = now.AddDays(-2) });
        trade.Negotiations.Add(new TradeNegotiation { RevisionNo = 1, Action = NegotiationAction.Submitted, ByFranchiseId = mum.Id, ByUserId = users[RoleCatalog.CricketDirector].Id, ByUserName = users[RoleCatalog.CricketDirector].FullName, Message = "We think this balances both squads. Open to discussion.", At = now.AddDays(-1) });
        trade.Negotiations.Add(new TradeNegotiation { RevisionNo = 1, Action = NegotiationAction.Note, ByFranchiseId = mum.Id, ByUserId = users[RoleCatalog.Ceo].Id, ByUserName = users[RoleCatalog.Ceo].FullName, Message = "Internal: we can go up to ₹1 Cr consideration if needed.", IsInternal = true, At = now.AddDays(-1) });
        db.TradeOffers.Add(trade);

        var emerging = profiles.Where(p => p.Franchise is null && Today.Year - p.Player.DateOfBirth.Year <= 23).OrderByDescending(p => Math.Max(p.Bat, p.Bowl)).Take(3).ToList();
        var scout = users[RoleCatalog.Scout];
        if (emerging.Count > 0)
        {
            var assignment = new ScoutingAssignment
            {
                FranchiseId = mum.Id, PlayerId = emerging[0].Player.Id, Title = $"Watch {emerging[0].Player.FullName} in domestic T20", AssignedToUserId = scout.Id,
                Description = "Assess temperament under pressure and death-overs skills.", DueDate = Today.AddDays(10), Status = AssignmentStatus.InProgress,
                CreatedAt = now.AddDays(-5), CreatedBy = users[RoleCatalog.ScoutingManager].Id,
            };
            db.ScoutingAssignments.Add(assignment);
            var i = 0;
            foreach (var e in emerging)
            {
                db.PlayerScoutingReports.Add(new PlayerScoutingReport
                {
                    FranchiseId = mum.Id, PlayerId = e.Player.Id, ScoutUserId = i == 2 ? scout2.Id : scout.Id, AssignmentId = i == 0 ? assignment.Id : null,
                    MatchObserved = $"Demo Domestic T20 Trophy vs {rng.Pick(domesticTeams)}", ObservedOn = Today.AddDays(-rng.Int(3, 40)),
                    Strengths = "Clean striking through the line; calm between overs.", Weaknesses = "Short ball outside off stump; fitness in the field.",
                    Observations = "Synthetic demo report.", TechniqueRating = rng.Int(6, 9), TemperamentRating = rng.Int(5, 9), FitnessRating = rng.Int(5, 8),
                    PotentialRating = rng.Int(7, 10), OverallRating = rng.Int(6, 9), Recommendation = i == 0 ? ScoutingRecommendation.Sign : ScoutingRecommendation.StrongMonitor,
                    Status = i == 1 ? ReportStatus.Reviewed : ReportStatus.Submitted, ReviewedBy = i == 1 ? users[RoleCatalog.ScoutingManager].Id : null,
                    ReviewedAt = i == 1 ? now.AddDays(-1) : null, ReviewComment = i == 1 ? "Agree — add to the auction shortlist." : null, CreatedAt = now.AddDays(-rng.Int(1, 20)),
                });
                i++;
            }
        }
        var injured = mumSquad.First(p => p.Role == "bowler" && p != offered);
        db.PlayerMedicalRecords.Add(new PlayerMedicalRecord
        {
            PlayerId = injured.Player.Id, FranchiseId = mum.Id, Condition = "Side strain (demo)", BodyPart = "Left side", Severity = InjurySeverity.Moderate,
            Assessment = "Synthetic demo record: grade 1 strain, graduated return to bowling.", InjuryDate = Today.AddDays(-12), ExpectedReturn = Today.AddDays(18),
            RecoveryProgress = 45, CreatedAt = now.AddDays(-12), CreatedBy = users[RoleCatalog.Medical].Id,
        });
        var existingAvail = await db.PlayerAvailability.FirstOrDefaultAsync(a => a.PlayerId == injured.Player.Id, ct);
        if (existingAvail is null)
            db.PlayerAvailability.Add(new PlayerAvailability { PlayerId = injured.Player.Id, Status = AvailabilityStatus.Injured, PublicNote = "Expected back before the season", ExpectedReturn = Today.AddDays(18), UpdatedAt = now });
        injured.Player.Status = PlayerStatus.Injured;

        var internalThread = new MessageThread { Subject = "Auction shortlist", Type = ThreadType.Internal, FranchiseId = mum.Id, LastMessageAt = now.AddHours(-3), CreatedAt = now.AddDays(-1) };
        internalThread.Messages.Add(new Message { SenderUserId = users[RoleCatalog.CricketDirector].Id, SenderName = users[RoleCatalog.CricketDirector].FullName, SenderFranchiseId = mum.Id, Body = "Let's finalise the top 10 auction targets by Friday.", SentAt = now.AddHours(-5) });
        internalThread.Messages.Add(new Message { SenderUserId = users[RoleCatalog.AuctionManager].Id, SenderName = users[RoleCatalog.AuctionManager].FullName, SenderFranchiseId = mum.Id, Body = "Draft list is in the Valuation screen. We need a back-up keeper.", SentAt = now.AddHours(-3) });
        db.MessageThreads.Add(internalThread);
        var external = new MessageThread { Subject = $"Trade {trade.Reference}", Type = ThreadType.Trade, FranchiseId = mum.Id, CounterpartyFranchiseId = chn.Id, TradeOfferId = trade.Id, LastMessageAt = now.AddHours(-20), CreatedAt = now.AddDays(-1) };
        external.Messages.Add(new Message { SenderUserId = users[RoleCatalog.CricketDirector].Id, SenderName = users[RoleCatalog.CricketDirector].FullName, SenderFranchiseId = mum.Id, Body = "Happy to talk through the offer whenever suits.", SentAt = now.AddHours(-20) });
        db.MessageThreads.Add(external);

        db.BudgetAllocations.AddRange(
            new BudgetAllocation { FranchiseId = mum.Id, SeasonId = current.Id, Category = FinanceCategory.OtherExpense, Label = "Travel & accommodation", Amount = 6 * Money.Crore, UpdatedAt = now },
            new BudgetAllocation { FranchiseId = mum.Id, SeasonId = current.Id, Category = FinanceCategory.OtherExpense, Label = "Training camps", Amount = 2 * Money.Crore, UpdatedAt = now },
            new BudgetAllocation { FranchiseId = mum.Id, SeasonId = current.Id, Category = FinanceCategory.PlayerCompensation, Label = "Player salaries", Amount = mumSquad.Sum(p => p.Price), UpdatedAt = now });
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            FranchiseId = mum.Id, SeasonId = current.Id, Category = FinanceCategory.OtherExpense, Direction = FinanceDirection.Debit, Amount = 85 * Money.Lakh,
            Description = "Pre-season training camp (demo)", ReferenceType = "Manual", IdempotencyKey = "demo:camp", CreatedAt = now.AddDays(-20), CreatedBy = users[RoleCatalog.FinanceManager].Id,
        });
        db.AuditLogs.Add(new AuditLog { At = now, Action = "system.demo_seeded", EntityType = "System", Details = "{\"note\":\"Fictional demo league created\"}" });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Demo league ready: {Players} players, {Matches} matches, {Lines} performance rows.", profiles.Count, matches.Count, lines.Count);
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
    private async Task BulkInsertAsync<T>(IReadOnlyList<T> rows, string table, CancellationToken ct)
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

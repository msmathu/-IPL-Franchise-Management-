namespace Ipl.Domain;

/// <summary>Every permission in the platform. Checked on the server for every endpoint.</summary>
public static class Perm
{
    public const string FranchisesRead = "franchises.read";
    public const string FranchisesEdit = "franchises.edit";
    public const string FranchisesManage = "franchises.manage";
    public const string PlayersRead = "players.read";
    public const string PlayersManage = "players.manage";
    public const string StatsRead = "stats.read";
    public const string StatsImport = "stats.import";
    public const string ComparisonsRead = "comparisons.read";
    public const string ComparisonsManage = "comparisons.manage";
    public const string TradesRead = "trades.read";
    public const string TradesCreate = "trades.create";
    public const string TradesApprove = "trades.approve";
    public const string TradesConsent = "trades.consent";
    public const string TradesLeagueApprove = "trades.league_approve";
    public const string RetentionRead = "retention.read";
    public const string RetentionManage = "retention.manage";
    public const string RetentionSubmit = "retention.submit";
    public const string AuctionsRead = "auctions.read";
    public const string AuctionsManage = "auctions.manage";
    public const string AuctionsBid = "auctions.bid";
    public const string SquadRead = "squad.read";
    public const string SquadManage = "squad.manage";
    public const string PlayingXIManage = "playingxi.manage";
    public const string ScoutingRead = "scouting.read";
    public const string ScoutingReport = "scouting.report";
    public const string ScoutingManage = "scouting.manage";
    public const string ValuationRead = "valuation.read";
    public const string ValuationRun = "valuation.run";
    public const string FinanceRead = "finance.read";
    public const string FinanceManage = "finance.manage";
    public const string FinanceApprove = "finance.approve";
    public const string ContractsRead = "contracts.read";
    public const string ContractsManage = "contracts.manage";
    public const string MessagesRead = "messages.read";
    public const string MessagesSend = "messages.send";
    public const string MedicalRead = "medical.read";
    public const string MedicalManage = "medical.manage";
    public const string AvailabilityRead = "availability.read";
    public const string AvailabilityManage = "availability.manage";
    public const string ReportsRead = "reports.read";
    public const string ReportsExport = "reports.export";
    public const string UsersManage = "users.manage";
    public const string UsersAdmin = "users.admin";
    public const string SettingsManage = "settings.manage";
    public const string SeasonsManage = "seasons.manage";
    public const string AuditRead = "audit.read";

    public static readonly (string Code, string Module, string Description)[] All =
    [
        (FranchisesRead, "Franchises", "View franchise profiles, staff and history"),
        (FranchisesEdit, "Franchises", "Edit own franchise profile and staff"),
        (FranchisesManage, "Franchises", "Create, activate and deactivate franchises (league)"),
        (PlayersRead, "Players", "View player profiles and search the player database"),
        (PlayersManage, "Players", "Create, edit and import players"),
        (StatsRead, "Statistics", "View player statistics and performance analysis"),
        (StatsImport, "Statistics", "Import statistics from authorised sources (CSV)"),
        (ComparisonsRead, "Comparison", "Compare players and view saved comparisons"),
        (ComparisonsManage, "Comparison", "Save and share comparison reports"),
        (TradesRead, "Trading", "View trades involving the franchise"),
        (TradesCreate, "Trading", "Propose, negotiate, counter and withdraw trades"),
        (TradesApprove, "Trading", "Authorise trades on behalf of the franchise"),
        (TradesConsent, "Trading", "Record player consent for trades"),
        (TradesLeagueApprove, "Trading", "Approve trades as the league and complete transfers"),
        (RetentionRead, "Retention", "View retention plans and releases"),
        (RetentionManage, "Retention", "Build retention simulations and releases"),
        (RetentionSubmit, "Retention", "Submit the final retention list"),
        (AuctionsRead, "Auction", "Watch auctions and view results"),
        (AuctionsManage, "Auction", "Configure and run auctions (league)"),
        (AuctionsBid, "Auction", "Bid in live auctions for the franchise"),
        (SquadRead, "Squad", "View the squad and composition"),
        (SquadManage, "Squad", "Manage squad records"),
        (PlayingXIManage, "Squad", "Build and save Playing XI combinations"),
        (ScoutingRead, "Scouting", "View all scouting reports of the franchise"),
        (ScoutingReport, "Scouting", "Write scouting reports"),
        (ScoutingManage, "Scouting", "Create assignments and review reports"),
        (ValuationRead, "Valuation", "View model valuations and recommendations"),
        (ValuationRun, "Valuation", "Re-run valuation models"),
        (FinanceRead, "Finance", "View purse, budgets and financial reports"),
        (FinanceManage, "Finance", "Record expenses, budgets and payments"),
        (FinanceApprove, "Finance", "Approve financial transactions"),
        (ContractsRead, "Contracts", "View player contracts"),
        (ContractsManage, "Contracts", "Create and change player contracts"),
        (MessagesRead, "Communication", "Read franchise messages"),
        (MessagesSend, "Communication", "Send messages"),
        (MedicalRead, "Medical", "View detailed medical records"),
        (MedicalManage, "Medical", "Record injuries, assessments and clearances"),
        (AvailabilityRead, "Medical", "View player availability status"),
        (AvailabilityManage, "Medical", "Update player availability status"),
        (ReportsRead, "Reports", "View dashboards and reports"),
        (ReportsExport, "Reports", "Export reports (PDF, Excel, CSV)"),
        (UsersManage, "Users", "Invite and manage users of the franchise"),
        (UsersAdmin, "Users", "Manage all users and roles (platform)"),
        (SettingsManage, "Settings", "Change season rules and platform settings"),
        (SeasonsManage, "Settings", "Create and manage seasons"),
        (AuditRead, "Audit", "View audit logs"),
    ];
}

/// <summary>The 15 roles and their default permissions. Seeded into Roles/RolePermissions.</summary>
public static class RoleCatalog
{
    public const string SuperAdmin = "super_admin";
    public const string LeagueAdmin = "league_admin";
    public const string Owner = "franchise_owner";
    public const string Ceo = "ceo";
    public const string CricketDirector = "cricket_director";
    public const string HeadCoach = "head_coach";
    public const string AssistantCoach = "assistant_coach";
    public const string AuctionManager = "auction_manager";
    public const string ScoutingManager = "scouting_manager";
    public const string Scout = "scout";
    public const string Analyst = "analyst";
    public const string FinanceManager = "finance_manager";
    public const string Medical = "medical_staff";
    public const string Operations = "team_operations";
    public const string Viewer = "viewer";

    private static readonly string[] BaseRead =
        [Perm.FranchisesRead, Perm.PlayersRead, Perm.StatsRead, Perm.AvailabilityRead, Perm.MessagesRead, Perm.MessagesSend];

    private static string[] With(params string[] extra) => [.. BaseRead, .. extra];

    public static readonly (string Code, string Name, RoleScope Scope, string Description, string[] Permissions)[] All =
    [
        (SuperAdmin, "Platform Super Admin", RoleScope.Platform, "Full access to the whole platform",
            Perm.All.Select(p => p.Code).ToArray()),
        (LeagueAdmin, "League Administrator", RoleScope.League, "Runs seasons, rules, auctions and league approvals (no medical details)",
            With(Perm.FranchisesManage, Perm.FranchisesEdit, Perm.PlayersManage, Perm.StatsImport, Perm.ComparisonsRead,
                Perm.TradesRead, Perm.TradesLeagueApprove, Perm.RetentionRead, Perm.AuctionsRead, Perm.AuctionsManage,
                Perm.SquadRead, Perm.ValuationRead, Perm.FinanceRead, Perm.ContractsRead, Perm.ReportsRead, Perm.ReportsExport,
                Perm.UsersAdmin, Perm.UsersManage, Perm.SettingsManage, Perm.SeasonsManage, Perm.AuditRead, Perm.AvailabilityManage)),
        (Owner, "Franchise Owner", RoleScope.Franchise, "Approves major transactions and sees everything except medical details",
            With(Perm.FranchisesEdit, Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.TradesRead, Perm.TradesCreate, Perm.TradesApprove,
                Perm.RetentionRead, Perm.RetentionManage, Perm.RetentionSubmit, Perm.AuctionsRead, Perm.AuctionsBid, Perm.SquadRead,
                Perm.ScoutingRead, Perm.ValuationRead, Perm.ValuationRun, Perm.FinanceRead, Perm.FinanceApprove, Perm.ContractsRead,
                Perm.ReportsRead, Perm.ReportsExport, Perm.UsersManage, Perm.AuditRead)),
        (Ceo, "CEO / General Manager", RoleScope.Franchise, "Runs the franchise: trades, retention, finance and contracts",
            With(Perm.FranchisesEdit, Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.TradesRead, Perm.TradesCreate, Perm.TradesApprove,
                Perm.RetentionRead, Perm.RetentionManage, Perm.RetentionSubmit, Perm.AuctionsRead, Perm.AuctionsBid, Perm.SquadRead, Perm.SquadManage,
                Perm.ScoutingRead, Perm.ValuationRead, Perm.ValuationRun, Perm.FinanceRead, Perm.FinanceManage, Perm.FinanceApprove,
                Perm.ContractsRead, Perm.ContractsManage, Perm.ReportsRead, Perm.ReportsExport, Perm.UsersManage, Perm.AuditRead)),
        (CricketDirector, "Cricket Director", RoleScope.Franchise, "Squad strategy, trades, scouting and auction targets",
            With(Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.TradesRead, Perm.TradesCreate, Perm.TradesConsent, Perm.RetentionRead,
                Perm.RetentionManage, Perm.RetentionSubmit, Perm.AuctionsRead, Perm.AuctionsBid, Perm.SquadRead, Perm.SquadManage,
                Perm.PlayingXIManage, Perm.ScoutingRead, Perm.ScoutingManage, Perm.ValuationRead, Perm.ValuationRun, Perm.ContractsRead,
                Perm.ReportsRead, Perm.ReportsExport)),
        (HeadCoach, "Head Coach", RoleScope.Franchise, "Squad selection, Playing XI and player form",
            With(Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.SquadRead, Perm.PlayingXIManage, Perm.ScoutingRead, Perm.ValuationRead,
                Perm.RetentionRead, Perm.AuctionsRead, Perm.ReportsRead)),
        (AssistantCoach, "Assistant Coach", RoleScope.Franchise, "Views squad, form and Playing XI",
            With(Perm.ComparisonsRead, Perm.SquadRead, Perm.AuctionsRead, Perm.ReportsRead)),
        (AuctionManager, "Auction Manager", RoleScope.Franchise, "Prepares auction targets and bids in live auctions",
            With(Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.AuctionsRead, Perm.AuctionsBid, Perm.RetentionRead, Perm.SquadRead,
                Perm.ValuationRead, Perm.ValuationRun, Perm.FinanceRead, Perm.ReportsRead, Perm.ReportsExport)),
        (ScoutingManager, "Scouting Manager", RoleScope.Franchise, "Runs the scouting team: assignments and report reviews",
            With(Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.ScoutingRead, Perm.ScoutingReport, Perm.ScoutingManage, Perm.ValuationRead,
                Perm.SquadRead, Perm.AuctionsRead, Perm.ReportsRead)),
        (Scout, "Scout", RoleScope.Franchise, "Submits scouting reports (sees own reports only)",
            With(Perm.ScoutingReport, Perm.ComparisonsRead)),
        (Analyst, "Cricket Analyst", RoleScope.Franchise, "Statistics, comparisons, valuations and reports",
            With(Perm.ComparisonsRead, Perm.ComparisonsManage, Perm.ValuationRead, Perm.ValuationRun, Perm.SquadRead, Perm.ScoutingRead,
                Perm.AuctionsRead, Perm.ReportsRead, Perm.ReportsExport, Perm.StatsImport)),
        (FinanceManager, "Finance Manager", RoleScope.Franchise, "Purse, contracts, payments and financial reports",
            With(Perm.FinanceRead, Perm.FinanceManage, Perm.ContractsRead, Perm.ContractsManage, Perm.AuctionsRead, Perm.TradesRead,
                Perm.RetentionRead, Perm.SquadRead, Perm.ReportsRead, Perm.ReportsExport)),
        (Medical, "Medical Staff", RoleScope.Franchise, "Medical records, fitness clearance and availability",
            With(Perm.MedicalRead, Perm.MedicalManage, Perm.AvailabilityManage, Perm.SquadRead)),
        (Operations, "Team Operations Manager", RoleScope.Franchise, "Squad records, availability, player consent and staff",
            With(Perm.SquadRead, Perm.SquadManage, Perm.TradesRead, Perm.TradesConsent, Perm.AvailabilityManage, Perm.ContractsRead,
                Perm.AuctionsRead, Perm.UsersManage, Perm.ReportsRead)),
        (Viewer, "Read-only Viewer", RoleScope.Franchise, "Read-only access to non-confidential data",
            With(Perm.SquadRead, Perm.AuctionsRead, Perm.ReportsRead)),
    ];
}

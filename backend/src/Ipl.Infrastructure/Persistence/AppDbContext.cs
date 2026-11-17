using System.Linq.Expressions;
using Ipl.Application;
using Ipl.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ipl.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null, IClock? clock = null)
    : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    // Evaluated per query by the global filters below.
    private bool ScopeToFranchise => currentUser is { IsAuthenticated: true, IsLeagueWide: false };
    private Guid? CurrentFranchiseId => currentUser?.FranchiseId;
    private bool ScopeToUser => currentUser is { IsAuthenticated: true };
    private Guid CurrentUserId => currentUser?.UserId ?? Guid.Empty;

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<SeasonRule> SeasonRules => Set<SeasonRule>();
    public DbSet<Franchise> Franchises => Set<Franchise>();
    public DbSet<FranchiseSeason> FranchiseSeasons => Set<FranchiseSeason>();
    public DbSet<FranchiseStaff> FranchiseStaff => Set<FranchiseStaff>();
    public DbSet<FranchiseMember> FranchiseMembers => Set<FranchiseMember>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<PlayerRoleType> PlayerRoles => Set<PlayerRoleType>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerTeam> PlayerTeams => Set<PlayerTeam>();
    public DbSet<PlayerAvailability> PlayerAvailability => Set<PlayerAvailability>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<PlayerMatchStatistic> PlayerMatchStatistics => Set<PlayerMatchStatistic>();
    public DbSet<PlayerSeasonStatistic> PlayerSeasonStatistics => Set<PlayerSeasonStatistic>();
    public DbSet<PlayerCareerStatistic> PlayerCareerStatistics => Set<PlayerCareerStatistic>();
    public DbSet<TradeOffer> TradeOffers => Set<TradeOffer>();
    public DbSet<TradeOfferPlayer> TradeOfferPlayers => Set<TradeOfferPlayer>();
    public DbSet<TradeNegotiation> TradeNegotiations => Set<TradeNegotiation>();
    public DbSet<TradeApproval> TradeApprovals => Set<TradeApproval>();
    public DbSet<TradeTransaction> TradeTransactions => Set<TradeTransaction>();
    public DbSet<FranchiseSquad> FranchiseSquads => Set<FranchiseSquad>();
    public DbSet<PlayingXI> PlayingXIs => Set<PlayingXI>();
    public DbSet<PlayingXIPlayer> PlayingXIPlayers => Set<PlayingXIPlayer>();
    public DbSet<RetentionPlan> RetentionPlans => Set<RetentionPlan>();
    public DbSet<RetentionRecord> RetentionRecords => Set<RetentionRecord>();
    public DbSet<ReleaseRecord> ReleaseRecords => Set<ReleaseRecord>();
    public DbSet<AuctionEvent> AuctionEvents => Set<AuctionEvent>();
    public DbSet<AuctionPlayer> AuctionPlayers => Set<AuctionPlayer>();
    public DbSet<AuctionBid> AuctionBids => Set<AuctionBid>();
    public DbSet<AuctionResult> AuctionResults => Set<AuctionResult>();
    public DbSet<PlayerContract> PlayerContracts => Set<PlayerContract>();
    public DbSet<ContractPayment> ContractPayments => Set<ContractPayment>();
    public DbSet<FinanceTransaction> FinanceTransactions => Set<FinanceTransaction>();
    public DbSet<BudgetAllocation> BudgetAllocations => Set<BudgetAllocation>();
    public DbSet<ScoutingAssignment> ScoutingAssignments => Set<ScoutingAssignment>();
    public DbSet<PlayerScoutingReport> PlayerScoutingReports => Set<PlayerScoutingReport>();
    public DbSet<PlayerValuation> PlayerValuations => Set<PlayerValuation>();
    public DbSet<PlayerComparison> PlayerComparisons => Set<PlayerComparison>();
    public DbSet<PlayerMedicalRecord> PlayerMedicalRecords => Set<PlayerMedicalRecord>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageThreadRead> MessageThreadReads => Set<MessageThreadRead>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 2);
        b.Properties<string>().HaveMaxLength(450);
        foreach (var enumType in typeof(Entity).Assembly.GetTypes().Where(t => t.IsEnum))
            b.Properties(enumType).HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.HasDefaultSchema("ipl");

        foreach (var et in m.Model.GetEntityTypes())
        {
            if (et.FindProperty("RowVersion") is { } rv && rv.ClrType == typeof(byte[]))
            {
                rv.IsConcurrencyToken = true;
                rv.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate;
                rv.SetColumnType("rowversion");
            }
        }

        // ASP.NET data-protection keys are long XML documents.
        m.Entity<DataProtectionKey>(e => Long(e, x => x.Xml));

        // ---------- Identity ----------
        m.Entity<User>(e =>
        {
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.NormalizedEmail).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(150);
            e.HasMany(x => x.Roles).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Role>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(50); });
        m.Entity<Permission>(e => { e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(60); });
        m.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(x => x.Permissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<UserRole>(e => e.HasIndex(x => new { x.UserId, x.RoleId, x.FranchiseId }).IsUnique());
        m.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.Property(x => x.TokenHash).HasMaxLength(88);
        });
        m.Entity<LoginHistory>(e => e.HasIndex(x => new { x.UserId, x.At }));
        m.Entity<UserToken>(e => { e.HasIndex(x => x.TokenHash).IsUnique(); e.Property(x => x.TokenHash).HasMaxLength(88); });
        m.Entity<EmailMessage>(e => { Long(e, x => x.Body); e.HasIndex(x => x.SentAt); });
        m.Entity<AuditLog>(e =>
        {
            Long(e, x => x.Details);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.FranchiseId, x.At });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        // ---------- League ----------
        m.Entity<Season>(e => e.HasIndex(x => x.Year).IsUnique());
        m.Entity<SeasonRule>(e =>
        {
            e.HasIndex(x => new { x.SeasonId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(80);
            Long(e, x => x.Value);
            e.HasOne(x => x.Season).WithMany(x => x.Rules).HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Franchise>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(10);
            e.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
        });
        m.Entity<FranchiseSeason>(e =>
        {
            e.HasIndex(x => new { x.FranchiseId, x.SeasonId }).IsUnique();
            e.Property(x => x.NetRunRate).HasPrecision(6, 3);
            e.HasOne(x => x.Franchise).WithMany(x => x.Seasons).HasForeignKey(x => x.FranchiseId);
        });
        m.Entity<FranchiseStaff>(e => e.HasOne(x => x.Franchise).WithMany(x => x.Staff).HasForeignKey(x => x.FranchiseId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<FranchiseMember>(e => e.HasIndex(x => new { x.FranchiseId, x.UserId }).IsUnique());
        m.Entity<SystemSetting>(e => { e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(100); Long(e, x => x.Value); });

        // ---------- Players & statistics ----------
        m.Entity<Country>(e => e.HasIndex(x => x.Code).IsUnique());
        m.Entity<PlayerRoleType>(e => e.HasIndex(x => x.Code).IsUnique());
        m.Entity<Player>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CurrentFranchiseId);
            e.Property(x => x.FullName).HasMaxLength(150);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.HasOne(x => x.PrimaryRole).WithMany().HasForeignKey(x => x.PrimaryRoleId);
            e.HasOne(x => x.SecondaryRole).WithMany().HasForeignKey(x => x.SecondaryRoleId);
            e.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
        });
        m.Entity<PlayerTeam>(e => e.HasIndex(x => new { x.PlayerId, x.SeasonId }));
        m.Entity<PlayerAvailability>(e => e.HasIndex(x => x.PlayerId).IsUnique());
        m.Entity<Competition>(e => e.HasIndex(x => x.Code).IsUnique());
        m.Entity<Match>(e => e.HasIndex(x => new { x.CompetitionId, x.Year }));
        m.Entity<PlayerMatchStatistic>(e =>
        {
            e.HasIndex(x => new { x.MatchId, x.PlayerId }).IsUnique();
            e.HasIndex(x => x.PlayerId);
            e.HasOne(x => x.Match).WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<PlayerSeasonStatistic>(e => e.HasIndex(x => new { x.PlayerId, x.CompetitionId, x.Year }).IsUnique());
        m.Entity<PlayerCareerStatistic>(e => e.HasIndex(x => new { x.PlayerId, x.CompetitionId }).IsUnique());

        // ---------- Trading ----------
        m.Entity<TradeOffer>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.InitiatorFranchiseId, x.Status });
            e.HasIndex(x => new { x.CounterpartyFranchiseId, x.Status });
            Long(e, x => x.Conditions);
            e.HasOne(x => x.InitiatorFranchise).WithMany().HasForeignKey(x => x.InitiatorFranchiseId);
            e.HasOne(x => x.CounterpartyFranchise).WithMany().HasForeignKey(x => x.CounterpartyFranchiseId);
            e.HasMany(x => x.Players).WithOne(x => x.TradeOffer).HasForeignKey(x => x.TradeOfferId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Negotiations).WithOne(x => x.TradeOffer).HasForeignKey(x => x.TradeOfferId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Approvals).WithOne(x => x.TradeOffer).HasForeignKey(x => x.TradeOfferId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<TradeOfferPlayer>(e => e.HasIndex(x => new { x.TradeOfferId, x.PlayerId }).IsUnique());
        m.Entity<TradeNegotiation>(e => { Long(e, x => x.Message); Long(e, x => x.TermsSnapshot); });
        m.Entity<TradeApproval>(e => Long(e, x => x.Comment));
        m.Entity<TradeTransaction>(e => { e.HasIndex(x => x.TradeOfferId).IsUnique(); Long(e, x => x.Summary); });

        // ---------- Squads, retention ----------
        m.Entity<FranchiseSquad>(e =>
        {
            // A player can be in only one active squad per season.
            e.HasIndex(x => new { x.SeasonId, x.PlayerId }).IsUnique().HasFilter("[Status] = 'Active'");
            e.HasIndex(x => new { x.FranchiseId, x.SeasonId, x.Status });
        });
        m.Entity<PlayingXI>(e =>
        {
            Long(e, x => x.Notes);
            e.HasMany(x => x.Players).WithOne(x => x.PlayingXI).HasForeignKey(x => x.PlayingXIId).OnDelete(DeleteBehavior.Cascade);
            TenantFilter(e);
        });
        m.Entity<RetentionPlan>(e =>
        {
            e.HasMany(x => x.Records).WithOne(x => x.RetentionPlan).HasForeignKey(x => x.RetentionPlanId).OnDelete(DeleteBehavior.Cascade);
            TenantFilter(e);
        });
        m.Entity<RetentionRecord>(e => { e.HasIndex(x => new { x.FranchiseId, x.SeasonId, x.Status }); TenantFilter(e); });
        m.Entity<ReleaseRecord>(e => { e.HasIndex(x => new { x.FranchiseId, x.SeasonId }); Long(e, x => x.Reason); });

        // ---------- Auction ----------
        m.Entity<AuctionEvent>(e => e.HasMany(x => x.Lots).WithOne(x => x.AuctionEvent).HasForeignKey(x => x.AuctionEventId).OnDelete(DeleteBehavior.Cascade));
        m.Entity<AuctionPlayer>(e =>
        {
            e.HasIndex(x => new { x.AuctionEventId, x.PlayerId }).IsUnique();
            e.HasIndex(x => new { x.AuctionEventId, x.Status, x.SetNumber, x.LotNumber });
        });
        m.Entity<AuctionBid>(e =>
        {
            e.HasIndex(x => new { x.FranchiseId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.AuctionPlayerId, x.PlacedAt });
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.HasOne(x => x.AuctionPlayer).WithMany().HasForeignKey(x => x.AuctionPlayerId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<AuctionResult>(e =>
        {
            e.HasIndex(x => new { x.AuctionPlayerId, x.Round }).IsUnique();
            e.HasIndex(x => new { x.AuctionEventId, x.FranchiseId });
        });

        // ---------- Contracts & finance ----------
        m.Entity<PlayerContract>(e =>
        {
            Long(e, x => x.Notes);
            e.HasIndex(x => new { x.PlayerId, x.Status });
            e.HasMany(x => x.Payments).WithOne(x => x.Contract).HasForeignKey(x => x.PlayerContractId).OnDelete(DeleteBehavior.Cascade);
            TenantFilter(e);
        });
        m.Entity<FinanceTransaction>(e =>
        {
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.FranchiseId, x.SeasonId, x.Category });
            e.Property(x => x.IdempotencyKey).HasMaxLength(120);
            e.Property(x => x.PurseBalanceAfter).HasPrecision(18, 2);
            TenantFilter(e);
        });
        m.Entity<BudgetAllocation>(e => { e.HasIndex(x => new { x.FranchiseId, x.SeasonId, x.Category, x.Label }).IsUnique(); TenantFilter(e); });

        // ---------- Scouting, valuation, comparison, medical ----------
        m.Entity<ScoutingAssignment>(e => { Long(e, x => x.Description); TenantFilter(e); });
        m.Entity<PlayerScoutingReport>(e =>
        {
            Long(e, x => x.Strengths); Long(e, x => x.Weaknesses); Long(e, x => x.Observations); Long(e, x => x.ReviewComment);
            e.HasIndex(x => new { x.FranchiseId, x.PlayerId });
            e.HasOne(x => x.Scout).WithMany().HasForeignKey(x => x.ScoutUserId);
            e.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
            TenantFilter(e);
        });
        m.Entity<PlayerValuation>(e => { Long(e, x => x.Explanation); e.HasIndex(x => new { x.PlayerId, x.SeasonId, x.CreatedAt }); });
        m.Entity<PlayerComparison>(e => { Long(e, x => x.PlayerIds); Long(e, x => x.Notes); TenantFilter(e); });
        m.Entity<PlayerMedicalRecord>(e => { Long(e, x => x.Assessment); e.HasIndex(x => new { x.PlayerId, x.InjuryDate }); TenantFilter(e); });

        // ---------- Communication ----------
        m.Entity<MessageThread>(e =>
        {
            e.HasIndex(x => new { x.FranchiseId, x.LastMessageAt });
            e.HasIndex(x => new { x.CounterpartyFranchiseId, x.LastMessageAt });
            e.HasMany(x => x.Messages).WithOne(x => x.Thread).HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter("Tenant", x => !ScopeToFranchise || x.FranchiseId == CurrentFranchiseId || x.CounterpartyFranchiseId == CurrentFranchiseId);
        });
        m.Entity<Message>(e =>
        {
            Long(e, x => x.Body);
            e.HasIndex(x => new { x.ThreadId, x.SentAt });
            e.HasQueryFilter("Tenant", x => !ScopeToFranchise || x.Thread!.FranchiseId == CurrentFranchiseId || x.Thread!.CounterpartyFranchiseId == CurrentFranchiseId);
        });
        m.Entity<MessageThreadRead>(e => e.HasKey(x => new { x.ThreadId, x.UserId }));
        m.Entity<Notification>(e =>
        {
            Long(e, x => x.Body);
            e.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.DedupKey }).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
            e.HasQueryFilter("Owner", x => !ScopeToUser || x.UserId == CurrentUserId);
        });
        m.Entity<NotificationPreference>(e => e.HasKey(x => new { x.UserId, x.Type }));
        m.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.Property(x => x.Content).HasColumnType("varbinary(max)");
            e.HasQueryFilter("SoftDelete", x => !x.IsDeleted);
            TenantFilter(e);
        });

        // Every relationship not explicitly configured above is RESTRICT (avoids SQL Server
        // multiple-cascade-path errors and accidental mass deletes); owned child collections cascade.
        foreach (var fk in m.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).Cast<Microsoft.EntityFrameworkCore.Metadata.IConventionForeignKey>())
        {
            if (fk.GetDeleteBehaviorConfigurationSource() != Microsoft.EntityFrameworkCore.Metadata.ConfigurationSource.Explicit)
                ((Microsoft.EntityFrameworkCore.Metadata.IMutableForeignKey)fk).DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    /// <summary>Confidential franchise data: franchise users only ever see their own franchise's rows.</summary>
    private void TenantFilter<T>(EntityTypeBuilder<T> e) where T : class, IFranchiseOwned =>
        e.HasQueryFilter("Tenant", x => !ScopeToFranchise || x.FranchiseId == CurrentFranchiseId);

    private static void Long<T>(EntityTypeBuilder<T> e, Expression<Func<T, string?>> prop) where T : class =>
        e.Property(prop).HasColumnType("nvarchar(max)").Metadata.SetMaxLength(null);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = clock?.UtcNow ?? DateTime.UtcNow;
        Guid? userId = currentUser is { IsAuthenticated: true } ? currentUser.UserId : null;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditable a)
            {
                if (entry.State == EntityState.Added)
                {
                    if (a.CreatedAt == default) a.CreatedAt = now;
                    a.CreatedBy ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAt = now;
                    a.UpdatedBy = userId;
                }
            }
            if (entry.Entity is ISoftDelete s && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                s.IsDeleted = true;
                s.DeletedAt = now;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}

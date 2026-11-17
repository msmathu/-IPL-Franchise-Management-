using System.Net;
using System.Net.Mail;
using Ipl.Application;
using Ipl.Application.Analytics;
using Ipl.Application.Auctions;
using Ipl.Application.Finance;
using Ipl.Application.Franchises;
using Ipl.Application.Identity;
using Ipl.Application.Operations;
using Ipl.Application.Players;
using Ipl.Application.Reports;
using Ipl.Application.Rules;
using Ipl.Application.Scouting;
using Ipl.Application.Squads;
using Ipl.Application.Statistics;
using Ipl.Application.Trading;
using Ipl.Domain;
using Ipl.Infrastructure.Persistence;
using Ipl.Infrastructure.Reports;
using Ipl.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ipl.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connection, sql =>
        {
            sql.EnableRetryOnFailure(3);
            sql.CommandTimeout(60);
            sql.MigrationsHistoryTable("__EFMigrationsHistory", "ipl");
        }).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        if (config["Redis:Connection"] is { Length: > 0 } redis)
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "ipl:"; });
        else
            services.AddDistributedMemoryCache();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton(config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions());
        services.AddSingleton(config.GetSection("DemoData").Get<DemoSeedOptions>() ?? new DemoSeedOptions());
        services.AddSingleton(config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions());
        services.AddScoped<IAppCache, AppCache>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEmailQueue, EmailQueue>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportQueries, DapperReportQueries>();
        services.AddScoped<IRulesService, RulesService>();

        // Application modules
        services.AddScoped<AuthService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<FranchiseService>();
        services.AddScoped<PlayerService>();
        services.AddScoped<StatisticsService>();
        services.AddScoped<RatingService>();
        services.AddScoped<ValuationService>();
        services.AddScoped<ComparisonService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<TradingService>();
        services.AddScoped<RetentionService>();
        services.AddScoped<SquadService>();
        services.AddScoped<AuctionService>();
        services.AddScoped<ScoutingService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<FinanceService>();
        services.AddScoped<MedicalService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<NotificationQueryService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ExportReportsService>();

        services.AddScoped<DemoSeeder>();
        services.AddHostedService<ScheduledJobs>();
        services.AddHostedService<EmailDispatcher>();
        return services;
    }

    /// <summary>Applies migrations, syncs roles/permissions and (optionally) seeds the demo league.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, bool migrate, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (migrate) await db.Database.MigrateAsync(ct);
        // Readers never block writers (and vice versa): standard for OLTP web apps on SQL Server.
        var rcsi = await db.Database.SqlQueryRaw<bool>("SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()").FirstAsync(ct);
        if (!rcsi)
        {
            var name = db.Database.GetDbConnection().Database;
            await db.Database.ExecuteSqlRawAsync($"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE", ct);
        }
        await CatalogSeeder.SyncAsync(db, ct);
        if (scope.ServiceProvider.GetRequiredService<DemoSeedOptions>().Enabled)
            await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(ct);
    }
}

public class EmailOptions
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@franchise.local";
    public bool EnableSsl { get; set; } = true;
}

/// <summary>Periodic jobs: expire trade offers, send deadline and payment reminders.</summary>
public class ScheduledJobs(IServiceScopeFactory scopes, ILogger<ScheduledJobs> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduled job failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var expired = await sp.GetRequiredService<TradingService>().ExpireOverdueAsync(ct);
        if (expired > 0) log.LogInformation("Expired {Count} trade offers", expired);
        await sp.GetRequiredService<FinanceService>().RemindDuePaymentsAsync(ct);
        await RetentionRemindersAsync(sp, ct);
    }

    /// <summary>7, 3 and 1 days before the retention deadline, franchises that haven't submitted are reminded (once each).</summary>
    private static async Task RetentionRemindersAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IAppDbContext>();
        var rules = sp.GetRequiredService<IRulesService>();
        var notify = sp.GetRequiredService<INotificationService>();
        var clock = sp.GetRequiredService<IClock>();
        var season = await rules.CurrentSeasonAsync(ct);
        var deadline = (await rules.ForSeasonAsync(season.Id, ct)).DateOrNull(RuleKeys.RetentionDeadline);
        if (deadline is null) return;
        var days = deadline.Value.DayNumber - clock.Today.DayNumber;
        if (days is not (7 or 3 or 1)) return;
        var pending = await db.Franchises.Where(f => f.IsActive && !db.RetentionPlans.Any(p => p.FranchiseId == f.Id && p.SeasonId == season.Id && p.IsSubmitted))
            .Select(f => f.Id).ToListAsync(ct);
        foreach (var fid in pending)
            await notify.NotifyFranchiseAsync(fid, "retention.deadline", $"Retention deadline in {days} day{(days == 1 ? "" : "s")}",
                $"Submit your retention list by {deadline:d MMM yyyy}.", "/retention", [Perm.RetentionSubmit], $"retention-deadline:{season.Id}:{fid}:{days}", ct);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Sends queued e-mails when SMTP is configured; otherwise they stay in the outbox (visible to admins/logs).</summary>
public class EmailDispatcher(IServiceScopeFactory scopes, EmailOptions options, ILogger<EmailDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            log.LogInformation("SMTP is not configured: e-mails are kept in the EmailMessages outbox table.");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var batch = await db.EmailMessages.Where(e => e.SentAt == null && e.Attempts < 5).OrderBy(e => e.CreatedAt).Take(20).ToListAsync(stoppingToken);
                using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
                {
                    EnableSsl = options.EnableSsl,
                    Credentials = options.Username is null ? null : new NetworkCredential(options.Username, options.Password),
                };
                foreach (var e in batch)
                {
                    try
                    {
                        await client.SendMailAsync(new MailMessage(options.From, e.To, e.Subject, e.Body), stoppingToken);
                        e.SentAt = DateTime.UtcNow;
                    }
                    catch (SmtpException ex)
                    {
                        e.Attempts++;
                        e.LastError = ex.Message;
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "E-mail dispatch failed");
            }
        }
    }
}

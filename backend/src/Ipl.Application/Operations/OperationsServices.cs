using Ipl.Application.Players;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Operations;

// =====================================================================
// Medical & availability
// =====================================================================

public record MedicalRecordDto(Guid Id, Guid PlayerId, string Player, string Condition, string BodyPart, string Severity, string Assessment,
    DateOnly InjuryDate, DateOnly? ExpectedReturn, int RecoveryProgress, bool FitnessCleared, DateTime? ClearedAt, DateTime CreatedAt);

public record SaveMedicalRecordRequest(Guid PlayerId, string Condition, string BodyPart, InjurySeverity Severity, string Assessment, DateOnly InjuryDate,
    DateOnly? ExpectedReturn, int RecoveryProgress, string? PublicNote);

public record AvailabilityRowDto(Guid PlayerId, string Player, string Role, bool IsOverseas, string Status, string? PublicNote, DateOnly? ExpectedReturn, DateTime? UpdatedAt);

public class MedicalService(IAppDbContext db, ICurrentUser me, IClock clock, IAuditService audit, PlayerService players, IRulesService rules, INotificationService notify)
{
    /// <summary>Availability of the squad: what every franchise user may see (no medical detail).</summary>
    public async Task<IReadOnlyList<AvailabilityRowDto>> SquadAvailabilityAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.AvailabilityRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        return await db.FranchiseSquads.AsNoTracking().Where(s => s.FranchiseId == fid && s.SeasonId == season.Id && s.Status == SquadMemberStatus.Active)
            .Select(s => new
            {
                s.PlayerId, s.Player!.FullName, Role = s.Player.PrimaryRole!.Name, s.Player.IsOverseas,
                A = db.PlayerAvailability.FirstOrDefault(a => a.PlayerId == s.PlayerId),
            })
            .OrderBy(x => x.A == null ? 0 : 1).ThenBy(x => x.FullName)
            .Select(x => new AvailabilityRowDto(x.PlayerId, x.FullName, x.Role, x.IsOverseas, x.A == null ? "Available" : x.A.Status.ToString(),
                x.A == null ? null : x.A.PublicNote, x.A == null ? null : x.A.ExpectedReturn, x.A == null ? null : x.A.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MedicalRecordDto>> RecordsAsync(Guid? playerId, bool activeOnly, CancellationToken ct)
    {
        me.Require(Perm.MedicalRead);
        var fid = me.RequireFranchise();
        var q = db.PlayerMedicalRecords.AsNoTracking().Where(r => r.FranchiseId == fid);
        if (playerId is { } pid) q = q.Where(r => r.PlayerId == pid);
        if (activeOnly) q = q.Where(r => !r.FitnessCleared);
        audit.Record("medical.records_viewed", "PlayerMedicalRecord", playerId, null, fid);
        await db.SaveChangesAsync(ct);
        return await q.OrderByDescending(r => r.InjuryDate)
            .Select(r => new MedicalRecordDto(r.Id, r.PlayerId, r.Player!.FullName, r.Condition, r.BodyPart, r.Severity.ToString(), r.Assessment, r.InjuryDate,
                r.ExpectedReturn, r.RecoveryProgress, r.FitnessCleared, r.ClearedAt, r.CreatedAt)).ToListAsync(ct);
    }

    public async Task<Guid> SaveRecordAsync(Guid? id, SaveMedicalRecordRequest req, CancellationToken ct)
    {
        me.Require(Perm.MedicalManage);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Condition)) throw new AppValidationException("condition", "Describe the condition.");
        if (req.RecoveryProgress is < 0 or > 100) throw new AppValidationException("recoveryProgress", "Use 0–100%.");
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PlayerId, ct) ?? throw new AppValidationException("playerId", "Unknown player.");
        if (player.CurrentFranchiseId != fid) throw new ForbiddenException("Medical records can only be kept for your own players.");
        PlayerMedicalRecord r;
        if (id is { } existing) r = await db.PlayerMedicalRecords.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Record");
        else db.PlayerMedicalRecords.Add(r = new PlayerMedicalRecord { FranchiseId = fid, PlayerId = req.PlayerId });
        r.Condition = req.Condition.Trim();
        r.BodyPart = req.BodyPart?.Trim() ?? "";
        r.Severity = req.Severity;
        r.Assessment = req.Assessment?.Trim() ?? "";
        r.InjuryDate = req.InjuryDate;
        r.ExpectedReturn = req.ExpectedReturn;
        r.RecoveryProgress = req.RecoveryProgress;
        audit.Record(id is null ? "medical.record_created" : "medical.record_updated", "PlayerMedicalRecord", r.Id, new { player.FullName, Severity = req.Severity.ToString() }, fid);
        await db.SaveChangesAsync(ct);
        // The public status only says "Injured" with an optional non-medical note.
        if (!r.FitnessCleared)
        {
            await players.SetAvailabilityAsync(req.PlayerId, AvailabilityStatus.Injured, req.PublicNote, req.ExpectedReturn, ct);
            await notify.NotifyFranchiseAsync(fid, "availability.changed", $"{player.FullName} is unavailable",
                req.ExpectedReturn is { } er ? $"Expected back around {er:d MMM}." : "Return date to be confirmed.", "/medical", [Perm.SquadRead], ct: ct);
            await db.SaveChangesAsync(ct);
        }
        return r.Id;
    }

    public async Task ClearAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.MedicalManage);
        var r = await db.PlayerMedicalRecords.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Record");
        if (r.FitnessCleared) throw new ConflictException("Already cleared.");
        r.FitnessCleared = true;
        r.ClearedAt = clock.UtcNow;
        r.ClearedBy = me.UserId;
        r.RecoveryProgress = 100;
        audit.Record("medical.fitness_cleared", "PlayerMedicalRecord", id, null, r.FranchiseId);
        await db.SaveChangesAsync(ct);
        if (!await db.PlayerMedicalRecords.AnyAsync(x => x.PlayerId == r.PlayerId && !x.FitnessCleared, ct))
        {
            await players.SetAvailabilityAsync(r.PlayerId, AvailabilityStatus.Available, null, null, ct);
            var name = await db.Players.Where(p => p.Id == r.PlayerId).Select(p => p.FullName).FirstAsync(ct);
            await notify.NotifyFranchiseAsync(r.FranchiseId, "availability.changed", $"{name} is available", "Fitness clearance recorded.", "/medical", [Perm.SquadRead], ct: ct);
            await db.SaveChangesAsync(ct);
        }
    }
}

// =====================================================================
// Communication
// =====================================================================

public record ThreadDto(Guid Id, string Subject, string Type, Guid FranchiseId, string Franchise, Guid? CounterpartyFranchiseId, string? Counterparty,
    Guid? TradeOfferId, DateTime LastMessageAt, string? LastMessage, int Unread);

public record MessageDto(Guid Id, Guid SenderUserId, string SenderName, Guid SenderFranchiseId, string SenderFranchise, string Body, DateTime SentAt, bool IsMine);

public record CreateThreadRequest(string Subject, ThreadType Type, Guid? CounterpartyFranchiseId, Guid? TradeOfferId, string Message);

public class MessagingService(IAppDbContext db, ICurrentUser me, IClock clock, IRealtimePublisher realtime, INotificationService notify, IAuditService audit)
{
    public async Task<IReadOnlyList<ThreadDto>> ThreadsAsync(CancellationToken ct)
    {
        me.Require(Perm.MessagesRead);
        var fid = me.RequireFranchise();
        var userId = me.UserId;
        var rows = await db.MessageThreads.AsNoTracking().Where(t => t.FranchiseId == fid || t.CounterpartyFranchiseId == fid)
            .OrderByDescending(t => t.LastMessageAt).Take(100)
            .Select(t => new
            {
                T = t,
                Franchise = db.Franchises.Where(f => f.Id == t.FranchiseId).Select(f => f.Name).First(),
                Counterparty = db.Franchises.Where(f => f.Id == t.CounterpartyFranchiseId).Select(f => f.Name).FirstOrDefault(),
                Last = t.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Body).FirstOrDefault(),
                ReadAt = db.MessageThreadReads.Where(r => r.ThreadId == t.Id && r.UserId == userId).Select(r => (DateTime?)r.LastReadAt).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.T.Id).ToList();
        var unread = await db.Messages.AsNoTracking().Where(m => ids.Contains(m.ThreadId) && m.SenderUserId != userId)
            .Select(m => new { m.ThreadId, m.SentAt }).ToListAsync(ct);
        return rows.Select(r => new ThreadDto(r.T.Id, r.T.Subject, r.T.Type.ToString(), r.T.FranchiseId, r.Franchise, r.T.CounterpartyFranchiseId, r.Counterparty,
            r.T.TradeOfferId, r.T.LastMessageAt, r.Last is { Length: > 100 } l ? l[..100] + "…" : r.Last,
            unread.Count(u => u.ThreadId == r.T.Id && (r.ReadAt == null || u.SentAt > r.ReadAt)))).ToList();
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        if (!me.Has(Perm.MessagesRead) || me.FranchiseId is null) return 0;
        return (await ThreadsAsync(ct)).Sum(t => t.Unread);
    }

    public async Task<IReadOnlyList<MessageDto>> MessagesAsync(Guid threadId, CancellationToken ct)
    {
        me.Require(Perm.MessagesRead);
        var t = await RequireThreadAsync(threadId, ct);
        var names = await db.Franchises.AsNoTracking().Where(f => f.Id == t.FranchiseId || f.Id == t.CounterpartyFranchiseId).ToDictionaryAsync(f => f.Id, f => f.ShortName, ct);
        var msgs = await db.Messages.AsNoTracking().Where(m => m.ThreadId == threadId).OrderBy(m => m.SentAt).Take(500).ToListAsync(ct);
        var read = await db.MessageThreadReads.FirstOrDefaultAsync(r => r.ThreadId == threadId && r.UserId == me.UserId, ct);
        if (read is null) db.MessageThreadReads.Add(new MessageThreadRead { ThreadId = threadId, UserId = me.UserId, LastReadAt = clock.UtcNow });
        else read.LastReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return msgs.Select(m => new MessageDto(m.Id, m.SenderUserId, m.SenderName, m.SenderFranchiseId, names.GetValueOrDefault(m.SenderFranchiseId, ""), m.Body, m.SentAt, m.SenderUserId == me.UserId)).ToList();
    }

    public async Task<Guid> CreateThreadAsync(CreateThreadRequest req, CancellationToken ct)
    {
        me.Require(Perm.MessagesSend);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Message)) throw new AppValidationException("subject", "Enter a subject and a message.");
        if (req.Type == ThreadType.Internal && req.CounterpartyFranchiseId is not null) throw new AppValidationException("type", "Internal threads stay inside your franchise.");
        if (req.Type != ThreadType.Internal)
        {
            if (req.CounterpartyFranchiseId is not { } cp || cp == fid || !await db.Franchises.AnyAsync(f => f.Id == cp && f.IsActive, ct))
                throw new AppValidationException("counterpartyFranchiseId", "Choose another franchise.");
            if (req.Type == ThreadType.Trade)
            {
                var trade = await db.TradeOffers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.TradeOfferId, ct) ?? throw new AppValidationException("tradeOfferId", "Unknown trade.");
                if (!trade.IsParty(fid) || !trade.IsParty(cp)) throw new ForbiddenException("Both franchises must be part of the trade.");
            }
        }
        var now = clock.UtcNow;
        var thread = new MessageThread { Subject = req.Subject.Trim(), Type = req.Type, FranchiseId = fid, CounterpartyFranchiseId = req.CounterpartyFranchiseId, TradeOfferId = req.TradeOfferId, LastMessageAt = now };
        db.MessageThreads.Add(thread);
        await db.SaveChangesAsync(ct);
        await PostAsync(thread.Id, req.Message, ct);
        audit.Record("messages.thread_created", "MessageThread", thread.Id, new { thread.Subject, Type = req.Type.ToString(), req.CounterpartyFranchiseId }, fid);
        await db.SaveChangesAsync(ct);
        return thread.Id;
    }

    public async Task<MessageDto> PostAsync(Guid threadId, string body, CancellationToken ct)
    {
        me.Require(Perm.MessagesSend);
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) throw new AppValidationException("body", "Messages are 1–4000 characters.");
        var t = await RequireThreadAsync(threadId, ct);
        var fid = me.RequireFranchise();
        var now = clock.UtcNow;
        var msg = new Message { ThreadId = threadId, SenderUserId = me.UserId, SenderName = me.Name, SenderFranchiseId = fid, Body = body.Trim(), SentAt = now };
        db.Messages.Add(msg);
        var tracked = await db.MessageThreads.FirstAsync(x => x.Id == threadId, ct);
        tracked.LastMessageAt = now;
        var read = await db.MessageThreadReads.FirstOrDefaultAsync(r => r.ThreadId == threadId && r.UserId == me.UserId, ct);
        if (read is null) db.MessageThreadReads.Add(new MessageThreadRead { ThreadId = threadId, UserId = me.UserId, LastReadAt = now });
        else read.LastReadAt = now;
        await db.SaveChangesAsync(ct);
        var payload = new { ThreadId = threadId, msg.Id, msg.SenderName, msg.Body, msg.SentAt, msg.SenderUserId };
        await realtime.ToFranchise(t.FranchiseId, "message", payload);
        if (t.CounterpartyFranchiseId is { } cp)
        {
            await realtime.ToFranchise(cp, "message", payload);
            if (cp != fid || t.FranchiseId != fid)
            {
                var other = fid == t.FranchiseId ? cp : t.FranchiseId;
                await notify.NotifyFranchiseAsync(other, "message.received", $"Message: {t.Subject}", body.Length > 120 ? body[..120] + "…" : body,
                    $"/messages/{threadId}", [Perm.MessagesRead], ct: ct);
                await db.SaveChangesAsync(ct);
            }
        }
        var senderFranchise = await db.Franchises.Where(f => f.Id == fid).Select(f => f.ShortName).FirstAsync(ct);
        return new MessageDto(msg.Id, msg.SenderUserId, msg.SenderName, fid, senderFranchise, msg.Body, msg.SentAt, true);
    }

    private async Task<MessageThread> RequireThreadAsync(Guid id, CancellationToken ct)
    {
        var t = await db.MessageThreads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Conversation");
        var fid = me.FranchiseId;
        // League staff are not party to confidential franchise conversations.
        if (fid is null || (t.FranchiseId != fid && t.CounterpartyFranchiseId != fid)) throw new NotFoundException("Conversation");
        return t;
    }
}

// =====================================================================
// Notifications
// =====================================================================

public record NotificationDto(Guid Id, string Type, string Title, string Body, string? Link, DateTime CreatedAt, bool IsRead);

public record PreferenceDto(string Type, string Label, bool InApp, bool Email);

public class NotificationQueryService(IAppDbContext db, ICurrentUser me, IClock clock)
{
    public static readonly (string Type, string Label)[] Types =
    [
        ("trade.received", "New trade offers"), ("trade.countered", "Counteroffers"), ("trade.message", "Trade messages"),
        ("trade.progress", "Trade approvals and progress"), ("trade.completed", "Completed trades"), ("trade.league_approval", "League approvals needed"),
        ("auction.started", "Auction started"), ("auction.won", "Auction purchases"), ("retention.deadline", "Retention deadlines"),
        ("retention.submitted", "Retention submissions"), ("availability.changed", "Player availability"), ("scouting.assignment", "Scouting assignments"),
        ("scouting.report", "New scouting reports"), ("message.received", "Messages"), ("finance.payment_due", "Payments due"),
    ];

    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, PageRequest page, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me.UserId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null)).ToPagedAsync(page, ct);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct) => db.Notifications.CountAsync(n => n.UserId == me.UserId && n.ReadAt == null, ct);

    public async Task MarkReadAsync(Guid? id, CancellationToken ct)
    {
        var q = db.Notifications.Where(n => n.UserId == me.UserId && n.ReadAt == null);
        if (id is { } nid) q = q.Where(n => n.Id == nid);
        await q.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.UtcNow), ct);
    }

    public async Task<IReadOnlyList<PreferenceDto>> PreferencesAsync(CancellationToken ct)
    {
        var prefs = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == me.UserId).ToDictionaryAsync(p => p.Type, ct);
        return Types.Select(t => new PreferenceDto(t.Type, t.Label, prefs.GetValueOrDefault(t.Type)?.InApp ?? true, prefs.GetValueOrDefault(t.Type)?.Email ?? false)).ToList();
    }

    public async Task SavePreferencesAsync(IReadOnlyList<PreferenceDto> prefs, CancellationToken ct)
    {
        var existing = await db.NotificationPreferences.Where(p => p.UserId == me.UserId).ToDictionaryAsync(p => p.Type, ct);
        foreach (var p in prefs.Where(p => Types.Any(t => t.Type == p.Type)))
        {
            if (!existing.TryGetValue(p.Type, out var row)) db.NotificationPreferences.Add(row = new NotificationPreference { UserId = me.UserId, Type = p.Type });
            row.InApp = p.InApp;
            row.Email = p.Email;
        }
        await db.SaveChangesAsync(ct);
    }
}

// =====================================================================
// Audit log & settings
// =====================================================================

public record AuditLogDto(long Id, DateTime At, string? UserEmail, Guid? FranchiseId, string? Franchise, string Action, string EntityType, string? EntityId, string? Details, string? IpAddress);

public class AuditQueryService(IAppDbContext db, ICurrentUser me)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(string? action, string? entityType, string? user, DateTime? from, DateTime? to, Guid? franchiseId, PageRequest page, CancellationToken ct)
    {
        me.Require(Perm.AuditRead);
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!me.IsLeagueWide) { var fid = me.RequireFranchise(); q = q.Where(a => a.FranchiseId == fid); }
        else if (franchiseId is { } f) q = q.Where(a => a.FranchiseId == f);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.UserEmail != null && a.UserEmail.Contains(user));
        if (from is { } fr) q = q.Where(a => a.At >= fr);
        if (to is { } t) q = q.Where(a => a.At <= t);
        return await q.OrderByDescending(a => a.Id)
            .Select(a => new AuditLogDto(a.Id, a.At, a.UserEmail, a.FranchiseId, db.Franchises.Where(f => f.Id == a.FranchiseId).Select(f => f.ShortName).FirstOrDefault(),
                a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress))
            .ToPagedAsync(page, ct);
    }
}

public record SeasonDto(int Id, int Year, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent, string Status);

public record RuleDto(string Key, string Value, string Type, string Category, string Description, DateTime? UpdatedAt);

public record SaveSeasonRequest(int Year, string Name, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, int? CopyRulesFromSeasonId);

public class SettingsService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, IAppCache cache)
{
    public async Task<IReadOnlyList<SeasonDto>> SeasonsAsync(CancellationToken ct) =>
        await db.Seasons.AsNoTracking().OrderByDescending(s => s.Year)
            .Select(s => new SeasonDto(s.Id, s.Year, s.Name, s.StartDate, s.EndDate, s.IsCurrent, s.Status.ToString())).ToListAsync(ct);

    public async Task<int> CreateSeasonAsync(SaveSeasonRequest req, CancellationToken ct)
    {
        me.Require(Perm.SeasonsManage);
        if (await db.Seasons.AnyAsync(s => s.Year == req.Year, ct)) throw new ConflictException($"Season {req.Year} already exists.");
        if (req.EndDate <= req.StartDate) throw new AppValidationException("endDate", "The season must end after it starts.");
        var season = new Season { Year = req.Year, Name = req.Name.Trim(), StartDate = req.StartDate, EndDate = req.EndDate, Status = req.Status };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(ct);
        if (req.CopyRulesFromSeasonId is { } from)
        {
            var source = await db.SeasonRules.AsNoTracking().Where(r => r.SeasonId == from).ToListAsync(ct);
            foreach (var r in source)
                db.SeasonRules.Add(new SeasonRule { SeasonId = season.Id, Key = r.Key, Value = r.Value, ValueType = r.ValueType, Category = r.Category, Description = r.Description, UpdatedAt = clock.UtcNow, UpdatedBy = me.UserId });
        }
        audit.Record("settings.season_created", "Season", season.Id, req);
        await db.SaveChangesAsync(ct);
        return season.Id;
    }

    /// <summary>Makes a season current and gives every active franchise its purse for it (from that season's rules).</summary>
    public async Task SetCurrentAsync(int seasonId, CancellationToken ct)
    {
        me.Require(Perm.SeasonsManage);
        var season = await db.Seasons.FirstOrDefaultAsync(s => s.Id == seasonId, ct) ?? throw new NotFoundException("Season");
        var seasonRules = await rules.ForSeasonAsync(seasonId, ct);
        var purse = seasonRules.Decimal(RuleKeys.AuctionPurse);
        await db.Seasons.Where(s => s.IsCurrent).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCurrent, false), ct);
        season.IsCurrent = true;
        foreach (var fid in await db.Franchises.Where(f => f.IsActive).Select(f => f.Id).ToListAsync(ct))
        {
            if (await db.FranchiseSeasons.AnyAsync(s => s.FranchiseId == fid && s.SeasonId == seasonId, ct)) continue;
            db.FranchiseSeasons.Add(new FranchiseSeason { FranchiseId = fid, SeasonId = seasonId, PurseTotal = purse, PurseRemaining = purse });
            db.FinanceTransactions.Add(new FinanceTransaction
            {
                FranchiseId = fid, SeasonId = seasonId, Category = FinanceCategory.AuctionPurse, Direction = FinanceDirection.Credit, Amount = purse,
                PurseBalanceAfter = purse, Description = $"{season.Name} auction purse allocation", IdempotencyKey = $"purse-alloc:{fid}:{seasonId}",
                CreatedAt = clock.UtcNow, CreatedBy = me.UserId,
            });
        }
        audit.Record("settings.season_current", "Season", seasonId, null);
        await db.SaveChangesAsync(ct);
        await cache.BumpAsync("rules", ct);
    }

    public async Task<IReadOnlyList<RuleDto>> RulesAsync(int seasonId, CancellationToken ct)
    {
        var stored = await db.SeasonRules.AsNoTracking().Where(r => r.SeasonId == seasonId).ToDictionaryAsync(r => r.Key, ct);
        return RuleKeys.Definitions.Select(d => stored.TryGetValue(d.Key, out var r)
            ? new RuleDto(d.Key, r.Value, d.Type.ToString(), d.Category, d.Description, r.UpdatedAt)
            : new RuleDto(d.Key, "", d.Type.ToString(), d.Category, d.Description + " (not configured)", null)).ToList();
    }

    public async Task UpdateRuleAsync(int seasonId, string key, string value, CancellationToken ct)
    {
        me.Require(Perm.SettingsManage);
        rules.Validate(key, value.Trim());
        var def = RuleKeys.Definitions.First(d => d.Key == key);
        var rule = await db.SeasonRules.FirstOrDefaultAsync(r => r.SeasonId == seasonId && r.Key == key, ct);
        var before = rule?.Value;
        if (rule is null) db.SeasonRules.Add(rule = new SeasonRule { SeasonId = seasonId, Key = key, ValueType = def.Type, Category = def.Category, Description = def.Description });
        rule.Value = value.Trim();
        rule.UpdatedAt = clock.UtcNow;
        rule.UpdatedBy = me.UserId;
        audit.Record("settings.rule_changed", "SeasonRule", $"{seasonId}:{key}", new { from = before, to = rule.Value });
        await db.SaveChangesAsync(ct);
        await rules.InvalidateAsync(seasonId);
    }

    public async Task<string> RatingWeightsAsync(CancellationToken ct) =>
        await db.SystemSettings.Where(s => s.Key == "rating.weights").Select(s => s.Value).FirstOrDefaultAsync(ct)
        ?? System.Text.Json.JsonSerializer.Serialize(Analytics.RatingWeights.Default, JsonOptions.Default);

    public async Task SaveRatingWeightsAsync(Analytics.RatingWeights weights, CancellationToken ct)
    {
        me.Require(Perm.SettingsManage);
        var values = new[] { weights.RecentForm, weights.Career, weights.Age, weights.Consistency, weights.CompetitionLevel, weights.Availability, weights.Potential };
        if (values.Any(v => v < 0) || weights.Total <= 0) throw new AppValidationException("weights", "Weights must be zero or positive and not all zero.");
        var json = System.Text.Json.JsonSerializer.Serialize(weights, JsonOptions.Default);
        var s = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == "rating.weights", ct);
        if (s is null) db.SystemSettings.Add(s = new SystemSetting { Key = "rating.weights", Description = "Rating model weights" });
        var before = s.Value;
        s.Value = json;
        s.UpdatedAt = clock.UtcNow;
        audit.Record("settings.rating_weights", "SystemSetting", "rating.weights", new { from = before, to = json });
        await db.SaveChangesAsync(ct);
        Analytics.RatingService.Invalidate();
    }
}

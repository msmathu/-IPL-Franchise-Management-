using Ipl.Application.Analytics;
using Ipl.Application.Players;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Scouting;

public record ScoutDto(Guid Id, string FullName, string Role);

public record AssignmentDto(Guid Id, Guid? PlayerId, string? PlayerName, string Title, string Description, Guid AssignedToUserId, string AssignedTo,
    DateOnly? DueDate, string Status, int Reports, DateTime CreatedAt);

public record SaveAssignmentRequest(Guid? PlayerId, string Title, string Description, Guid AssignedToUserId, DateOnly? DueDate, AssignmentStatus Status);

public record ReportListItemDto(Guid Id, Guid PlayerId, string PlayerName, string Role, int? Age, string Scout, DateOnly ObservedOn, string MatchObserved,
    int OverallRating, string Recommendation, string Status, int Documents);

public record ReportDto(Guid Id, Guid PlayerId, string PlayerName, Guid ScoutUserId, string Scout, Guid? AssignmentId, string MatchObserved, DateOnly ObservedOn,
    string Strengths, string Weaknesses, string Observations, int TechniqueRating, int TemperamentRating, int FitnessRating, int PotentialRating,
    int OverallRating, string Recommendation, string Status, string? ReviewedBy, DateTime? ReviewedAt, string? ReviewComment,
    IReadOnlyList<DocumentDto> Documents, bool CanEdit, bool CanReview);

public record SaveReportRequest(Guid PlayerId, Guid? AssignmentId, string MatchObserved, DateOnly ObservedOn, string Strengths, string Weaknesses,
    string Observations, int TechniqueRating, int TemperamentRating, int FitnessRating, int PotentialRating, int OverallRating,
    ScoutingRecommendation Recommendation, bool Submit);

public record DocumentDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt);

public record EmergingPlayerDto(Guid PlayerId, string Name, int? Age, string Role, string Country, string? Franchise, decimal Rating, string Confidence,
    decimal PotentialScore, decimal FormScore, int Matches, int ScoutReports);

public class ScoutingService(IAppDbContext db, ICurrentUser me, IClock clock, IAuditService audit, INotificationService notify, RatingService ratings, DocumentService documents)
{
    // ---------------- Assignments ----------------

    public async Task<IReadOnlyList<AssignmentDto>> AssignmentsAsync(bool mineOnly, CancellationToken ct)
    {
        RequireAny(Perm.ScoutingReport, Perm.ScoutingRead);
        var fid = me.RequireFranchise();
        var q = db.ScoutingAssignments.AsNoTracking().Where(a => a.FranchiseId == fid);
        if (mineOnly || !me.Has(Perm.ScoutingRead)) q = q.Where(a => a.AssignedToUserId == me.UserId);
        return await q.OrderBy(a => a.Status).ThenBy(a => a.DueDate)
            .Select(a => new AssignmentDto(a.Id, a.PlayerId, a.Player != null ? a.Player.FullName : null, a.Title, a.Description, a.AssignedToUserId,
                a.AssignedTo!.FullName, a.DueDate, a.Status.ToString(), db.PlayerScoutingReports.Count(r => r.AssignmentId == a.Id), a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<Guid> SaveAssignmentAsync(Guid? id, SaveAssignmentRequest req, CancellationToken ct)
    {
        me.Require(Perm.ScoutingManage);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Title)) throw new AppValidationException("title", "Give the assignment a title.");
        if (!await db.UserRoles.AnyAsync(r => r.UserId == req.AssignedToUserId && r.FranchiseId == fid
                && r.Role!.Permissions.Any(p => p.Permission!.Code == Perm.ScoutingReport), ct))
            throw new AppValidationException("assignedToUserId", "Assign it to a scout of your franchise.");
        ScoutingAssignment a;
        if (id is { } existing)
            a = await db.ScoutingAssignments.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Assignment");
        else
            db.ScoutingAssignments.Add(a = new ScoutingAssignment { FranchiseId = fid });
        var reassigned = a.AssignedToUserId != req.AssignedToUserId;
        a.PlayerId = req.PlayerId;
        a.Title = req.Title.Trim();
        a.Description = req.Description?.Trim() ?? "";
        a.AssignedToUserId = req.AssignedToUserId;
        a.DueDate = req.DueDate;
        a.Status = req.Status;
        audit.Record(id is null ? "scouting.assignment_created" : "scouting.assignment_updated", "ScoutingAssignment", a.Id, new { a.Title }, fid);
        await db.SaveChangesAsync(ct);
        if (reassigned)
        {
            await notify.NotifyUsersAsync([req.AssignedToUserId], "scouting.assignment", "New scouting assignment", a.Title, "/scouting", fid, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        return a.Id;
    }

    /// <summary>Users of the franchise who can write scouting reports (for assignments).</summary>
    public async Task<IReadOnlyList<ScoutDto>> ScoutsAsync(CancellationToken ct)
    {
        me.Require(Perm.ScoutingManage);
        var fid = me.RequireFranchise();
        var rows = await db.UserRoles.AsNoTracking()
            .Where(r => r.FranchiseId == fid && r.User!.IsActive && r.Role!.Permissions.Any(p => p.Permission!.Code == Perm.ScoutingReport))
            .Select(r => new { r.UserId, r.User!.FullName, Role = r.Role!.Name }).ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).Select(g => new ScoutDto(g.Key, g.First().FullName, string.Join(", ", g.Select(x => x.Role)))).OrderBy(s => s.FullName).ToList();
    }

    // ---------------- Reports ----------------

    public async Task<PagedResult<ReportListItemDto>> ReportsAsync(Guid? playerId, string? status, PageRequest page, CancellationToken ct)
    {
        RequireAny(Perm.ScoutingReport, Perm.ScoutingRead);
        var fid = me.RequireFranchise();
        var today = clock.Today;
        var q = db.PlayerScoutingReports.AsNoTracking().Where(r => r.FranchiseId == fid);
        if (!me.Has(Perm.ScoutingRead)) q = q.Where(r => r.ScoutUserId == me.UserId); // scouts see their own reports only
        else q = q.Where(r => r.Status != ReportStatus.Draft || r.ScoutUserId == me.UserId);
        if (playerId is { } pid) q = q.Where(r => r.PlayerId == pid);
        if (Enum.TryParse<ReportStatus>(status, true, out var st)) q = q.Where(r => r.Status == st);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.ObservedOn).Skip((page.SafePage - 1) * page.SafeSize).Take(page.SafeSize)
            .Select(r => new
            {
                r.Id, r.PlayerId, r.Player!.FullName, Role = r.Player.PrimaryRole!.Name, r.Player.DateOfBirth, Scout = r.Scout!.FullName,
                r.ObservedOn, r.MatchObserved, r.OverallRating, r.Recommendation, r.Status,
                Docs = db.Documents.Count(d => d.OwnerType == "ScoutingReport" && d.OwnerId == r.Id),
            }).ToListAsync(ct);
        return new PagedResult<ReportListItemDto>(rows.Select(r => new ReportListItemDto(r.Id, r.PlayerId, r.FullName, r.Role, PlayerService.AgeOn(r.DateOfBirth, today),
            r.Scout, r.ObservedOn, r.MatchObserved, r.OverallRating, r.Recommendation.ToString(), r.Status.ToString(), r.Docs)).ToList(), page.SafePage, page.SafeSize, total);
    }

    public async Task<ReportDto> GetReportAsync(Guid id, CancellationToken ct)
    {
        RequireAny(Perm.ScoutingReport, Perm.ScoutingRead);
        var r = await db.PlayerScoutingReports.AsNoTracking().Include(x => x.Player).Include(x => x.Scout).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Report");
        RequireReportAccess(r);
        var reviewer = r.ReviewedBy is { } rb ? await db.Users.Where(u => u.Id == rb).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        var docs = await documents.ListAsync("ScoutingReport", id, ct);
        return new ReportDto(r.Id, r.PlayerId, r.Player!.FullName, r.ScoutUserId, r.Scout!.FullName, r.AssignmentId, r.MatchObserved, r.ObservedOn,
            r.Strengths, r.Weaknesses, r.Observations, r.TechniqueRating, r.TemperamentRating, r.FitnessRating, r.PotentialRating, r.OverallRating,
            r.Recommendation.ToString(), r.Status.ToString(), reviewer, r.ReviewedAt, r.ReviewComment, docs,
            r.ScoutUserId == me.UserId && r.Status != ReportStatus.Reviewed, me.Has(Perm.ScoutingManage) && r.Status == ReportStatus.Submitted);
    }

    public async Task<Guid> SaveReportAsync(Guid? id, SaveReportRequest req, CancellationToken ct)
    {
        me.Require(Perm.ScoutingReport);
        var fid = me.RequireFranchise();
        foreach (var (field, v) in new[] { ("techniqueRating", req.TechniqueRating), ("temperamentRating", req.TemperamentRating), ("fitnessRating", req.FitnessRating), ("potentialRating", req.PotentialRating), ("overallRating", req.OverallRating) })
            if (v is < 1 or > 10) throw new AppValidationException(field, "Ratings are from 1 to 10.");
        if (string.IsNullOrWhiteSpace(req.MatchObserved)) throw new AppValidationException("matchObserved", "Say which match or session you watched.");
        if (req.ObservedOn > clock.Today) throw new AppValidationException("observedOn", "The observation date cannot be in the future.");
        if (req.Submit && (string.IsNullOrWhiteSpace(req.Strengths) || string.IsNullOrWhiteSpace(req.Weaknesses)))
            throw new AppValidationException("strengths", "Describe strengths and weaknesses before submitting.");
        if (!await db.Players.AnyAsync(p => p.Id == req.PlayerId, ct)) throw new AppValidationException("playerId", "Unknown player.");

        PlayerScoutingReport r;
        if (id is { } existing)
        {
            r = await db.PlayerScoutingReports.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Report");
            if (r.ScoutUserId != me.UserId) throw new ForbiddenException("Only the author can edit a report.");
            if (r.Status == ReportStatus.Reviewed) throw new ConflictException("A reviewed report cannot be changed.");
        }
        else db.PlayerScoutingReports.Add(r = new PlayerScoutingReport { FranchiseId = fid, ScoutUserId = me.UserId });
        r.PlayerId = req.PlayerId;
        r.AssignmentId = req.AssignmentId;
        r.MatchObserved = req.MatchObserved.Trim();
        r.ObservedOn = req.ObservedOn;
        r.Strengths = req.Strengths?.Trim() ?? "";
        r.Weaknesses = req.Weaknesses?.Trim() ?? "";
        r.Observations = req.Observations?.Trim() ?? "";
        r.TechniqueRating = req.TechniqueRating;
        r.TemperamentRating = req.TemperamentRating;
        r.FitnessRating = req.FitnessRating;
        r.PotentialRating = req.PotentialRating;
        r.OverallRating = req.OverallRating;
        r.Recommendation = req.Recommendation;
        var submitted = req.Submit && r.Status == ReportStatus.Draft;
        if (req.Submit) r.Status = ReportStatus.Submitted;
        audit.Record(submitted ? "scouting.report_submitted" : "scouting.report_saved", "PlayerScoutingReport", r.Id, new { r.PlayerId }, fid);
        await db.SaveChangesAsync(ct);
        if (submitted)
        {
            await notify.NotifyFranchiseAsync(fid, "scouting.report", "New scouting report", $"{me.Name} submitted a report.", $"/scouting/reports/{r.Id}", [Perm.ScoutingManage], ct: ct);
            await db.SaveChangesAsync(ct);
        }
        return r.Id;
    }

    public async Task ReviewAsync(Guid id, string? comment, CancellationToken ct)
    {
        me.Require(Perm.ScoutingManage);
        var r = await db.PlayerScoutingReports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report");
        if (r.Status != ReportStatus.Submitted) throw new ConflictException("Only submitted reports can be reviewed.");
        r.Status = ReportStatus.Reviewed;
        r.ReviewedBy = me.UserId;
        r.ReviewedAt = clock.UtcNow;
        r.ReviewComment = comment?.Trim();
        audit.Record("scouting.report_reviewed", "PlayerScoutingReport", id, new { comment }, r.FranchiseId);
        await db.SaveChangesAsync(ct);
        await notify.NotifyUsersAsync([r.ScoutUserId], "scouting.reviewed", "Your report was reviewed", comment ?? "", $"/scouting/reports/{id}", r.FranchiseId, ct: ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteReportAsync(Guid id, CancellationToken ct)
    {
        me.Require(Perm.ScoutingReport);
        var r = await db.PlayerScoutingReports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report");
        if (r.ScoutUserId != me.UserId && !me.Has(Perm.ScoutingManage)) throw new ForbiddenException("Only the author or the scouting manager can delete a report.");
        if (r.Status == ReportStatus.Reviewed) throw new ConflictException("Reviewed reports are kept for the record.");
        db.PlayerScoutingReports.Remove(r);
        audit.Record("scouting.report_deleted", "PlayerScoutingReport", id, null, r.FranchiseId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DocumentDto> UploadAsync(Guid reportId, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        me.Require(Perm.ScoutingReport);
        var r = await db.PlayerScoutingReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reportId, ct) ?? throw new NotFoundException("Report");
        if (r.ScoutUserId != me.UserId && !me.Has(Perm.ScoutingManage)) throw new ForbiddenException("Only the author can attach documents.");
        return await documents.UploadAsync(r.FranchiseId, "ScoutingReport", reportId, fileName, contentType, content, ct);
    }

    public async Task<(string FileName, string ContentType, byte[] Content)> DownloadAsync(Guid reportId, Guid documentId, CancellationToken ct)
    {
        await GetReportAsync(reportId, ct); // access check
        return await documents.DownloadAsync("ScoutingReport", reportId, documentId, ct);
    }

    // ---------------- Talent discovery ----------------

    /// <summary>Young players ranked by the explainable rating model (potential + form weighted).</summary>
    public async Task<IReadOnlyList<EmergingPlayerDto>> EmergingAsync(int maxAge, string? category, bool freeAgentsOnly, CancellationToken ct)
    {
        RequireAny(Perm.ScoutingReport, Perm.ScoutingRead);
        var fid = me.FranchiseId;
        var born = clock.Today.AddYears(-maxAge - 1);
        var q = db.Players.AsNoTracking().Where(p => p.DateOfBirth > born && p.CareerStatus == CareerStatus.Active);
        q = PlayerService.ApplyCategory(q, category);
        if (freeAgentsOnly) q = q.Where(p => p.CurrentFranchiseId == null);
        var candidates = await q.Select(p => new { p.Id, p.Country!.Name, Franchise = p.CurrentFranchise != null ? p.CurrentFranchise.ShortName : null }).Take(400).ToListAsync(ct);
        var rated = await ratings.RateManyAsync(candidates.Select(c => c.Id).ToList(), ct);
        var reportCounts = await db.PlayerScoutingReports.AsNoTracking().Where(r => r.FranchiseId == fid).GroupBy(r => r.PlayerId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return candidates.Where(c => rated.ContainsKey(c.Id)).Select(c =>
        {
            var r = rated[c.Id];
            var potential = r.Factors.First(f => f.Factor == "Development potential").Score;
            var form = r.Factors.First(f => f.Factor == "Recent form").Score;
            return new EmergingPlayerDto(c.Id, r.PlayerName, r.Age, r.Role, c.Name, c.Franchise, r.Rating, r.Confidence, potential, form, r.MatchesUsed,
                reportCounts.GetValueOrDefault(c.Id));
        }).OrderByDescending(e => e.PotentialScore * 0.5m + e.Rating * 0.5m).Take(50).ToList();
    }

    private void RequireReportAccess(PlayerScoutingReport r)
    {
        me.RequireFranchiseAccess(r.FranchiseId);
        if (r.ScoutUserId == me.UserId) return;
        if (!me.Has(Perm.ScoutingRead) || r.Status == ReportStatus.Draft) throw new ForbiddenException("You can only see your own reports.");
    }

    private void RequireAny(params string[] perms)
    {
        if (!perms.Any(me.Has)) throw new ForbiddenException($"You need one of: {string.Join(", ", perms)}.");
    }
}

/// <summary>Stores uploads in the database (size/type limited) and serves them only after access checks by the caller.</summary>
public class DocumentService(IAppDbContext db, ICurrentUser me, IClock clock, IAuditService audit)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [".csv"] = "text/csv", [".txt"] = "text/plain",
    };

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(string ownerType, Guid ownerId, CancellationToken ct) =>
        await db.Documents.AsNoTracking().Where(d => d.OwnerType == ownerType && d.OwnerId == ownerId).OrderBy(d => d.UploadedAt)
            .Select(d => new DocumentDto(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt)).ToListAsync(ct);

    public async Task<DocumentDto> UploadAsync(Guid franchiseId, string ownerType, Guid ownerId, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        var safeName = Path.GetFileName(fileName).Trim();
        if (safeName.Length is 0 or > 200) throw new AppValidationException("file", "Invalid file name.");
        var ext = Path.GetExtension(safeName);
        if (!Allowed.TryGetValue(ext, out var type)) throw new AppValidationException("file", $"Allowed types: {string.Join(", ", Allowed.Keys)}.");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        if (ms.Length == 0) throw new AppValidationException("file", "The file is empty.");
        if (ms.Length > MaxBytes) throw new AppValidationException("file", "Files can be at most 5 MB.");
        var bytes = ms.ToArray();
        if (!MatchesSignature(ext, bytes)) throw new AppValidationException("file", "The file content does not match its type.");
        var doc = new Document
        {
            FranchiseId = franchiseId, OwnerType = ownerType, OwnerId = ownerId, FileName = safeName, ContentType = type, SizeBytes = bytes.Length,
            Content = bytes, UploadedBy = me.UserId, UploadedAt = clock.UtcNow,
        };
        db.Documents.Add(doc);
        audit.Record("documents.uploaded", ownerType, ownerId, new { safeName, bytes.Length }, franchiseId);
        await db.SaveChangesAsync(ct);
        return new DocumentDto(doc.Id, doc.FileName, doc.ContentType, doc.SizeBytes, doc.UploadedAt);
    }

    public async Task<(string FileName, string ContentType, byte[] Content)> DownloadAsync(string ownerType, Guid ownerId, Guid documentId, CancellationToken ct)
    {
        var d = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId && x.OwnerType == ownerType && x.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Document");
        audit.Record("documents.downloaded", ownerType, ownerId, new { d.FileName }, d.FranchiseId);
        await db.SaveChangesAsync(ct);
        return (d.FileName, d.ContentType, d.Content);
    }

    /// <summary>Checks magic bytes so a renamed executable can't be stored as a PDF/image.</summary>
    private static bool MatchesSignature(string ext, byte[] b) => ext.ToLowerInvariant() switch
    {
        ".pdf" => b.Length > 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46,
        ".png" => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
        ".jpg" or ".jpeg" => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8,
        ".docx" or ".xlsx" => b.Length > 4 && b[0] == 0x50 && b[1] == 0x4B,
        _ => !b.Take(512).Contains((byte)0),
    };
}

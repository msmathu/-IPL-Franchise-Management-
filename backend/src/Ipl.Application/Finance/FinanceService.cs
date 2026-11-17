using System.Data;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Finance;

public record LedgerSummaryDto(string Category, decimal Debits, decimal Credits, decimal Net, decimal? Budget, decimal? BudgetRemaining);

public record FinanceOverviewDto(Guid FranchiseId, int SeasonId, string Season, decimal PurseTotal, decimal PurseRemaining, decimal PurseUsed,
    decimal ContractCommitments, decimal PaidToPlayers, decimal ScheduledPayments, decimal OperatingBudget, IReadOnlyList<LedgerSummaryDto> Ledgers,
    IReadOnlyList<BudgetDto> Budgets);

public record TransactionDto(Guid Id, DateTime CreatedAt, string Category, string Direction, decimal Amount, decimal? PurseBalanceAfter, string Description,
    string? ReferenceType, string? ReferenceId);

public record BudgetDto(Guid Id, string Category, string Label, decimal Amount, decimal Spent);

public record SaveBudgetRequest(FinanceCategory Category, string Label, decimal Amount);

public record RecordExpenseRequest(string Description, decimal Amount, FinanceCategory Category, string? Reference, string IdempotencyKey);

public record PaymentDto(Guid Id, DateOnly DueDate, decimal Amount, string Status, DateTime? PaidAt);

public record ContractDto(Guid Id, Guid PlayerId, string Player, string Role, string StartSeason, string EndSeason, DateOnly StartDate, DateOnly EndDate,
    decimal AnnualSalary, string Acquisition, string Status, string? Notes, IReadOnlyList<PaymentDto> Payments);

public record SaveContractRequest(Guid PlayerId, int StartSeasonId, int EndSeasonId, DateOnly StartDate, DateOnly EndDate, decimal AnnualSalary,
    AcquisitionType Acquisition, string? Notes, int Instalments);

public class FinanceService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, IAuditService audit, INotificationService notify)
{
    /// <summary>Expenses above this need finance.approve (CEO/owner) to be booked directly.</summary>
    public const decimal ApprovalThreshold = 50 * Money.Lakh;

    public async Task<FinanceOverviewDto> OverviewAsync(Guid? franchiseId, CancellationToken ct)
    {
        me.Require(Perm.FinanceRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var fs = await db.FranchiseSeasons.AsNoTracking().FirstOrDefaultAsync(s => s.FranchiseId == fid && s.SeasonId == season.Id, ct)
            ?? throw new NotFoundException("Franchise season");
        var tx = await db.FinanceTransactions.AsNoTracking().Where(t => t.FranchiseId == fid && t.SeasonId == season.Id)
            .GroupBy(t => new { t.Category, t.Direction }).Select(g => new { g.Key.Category, g.Key.Direction, Total = g.Sum(t => t.Amount) }).ToListAsync(ct);
        var budgets = await BudgetsAsync(fid, season.Id, ct);
        var contracts = await db.PlayerContracts.AsNoTracking().Where(c => c.FranchiseId == fid && c.Status == ContractStatus.Active).SumAsync(c => c.AnnualSalary, ct);
        var payments = await db.ContractPayments.AsNoTracking().Where(p => p.Contract!.FranchiseId == fid)
            .GroupBy(p => p.Status).Select(g => new { g.Key, Total = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var ledgers = Enum.GetValues<FinanceCategory>().Select(c =>
        {
            var debit = tx.Where(t => t.Category == c && t.Direction == FinanceDirection.Debit).Sum(t => t.Total);
            var credit = tx.Where(t => t.Category == c && t.Direction == FinanceDirection.Credit).Sum(t => t.Total);
            var budget = budgets.Where(b => b.Category == c.ToString()).Sum(b => (decimal?)b.Amount);
            return new LedgerSummaryDto(c.ToString(), debit, credit, credit - debit, budget, budget is null ? null : budget - debit);
        }).ToList();
        return new FinanceOverviewDto(fid, season.Id, season.Name, fs.PurseTotal, fs.PurseRemaining, fs.PurseTotal - fs.PurseRemaining, contracts,
            payments.GetValueOrDefault(PaymentStatus.Paid), payments.GetValueOrDefault(PaymentStatus.Scheduled), fs.OperatingBudget, ledgers, budgets);
    }

    public async Task<PagedResult<TransactionDto>> TransactionsAsync(Guid? franchiseId, string? category, PageRequest page, CancellationToken ct)
    {
        me.Require(Perm.FinanceRead);
        var fid = me.RequireFranchise(franchiseId);
        var season = await rules.CurrentSeasonAsync(ct);
        var q = db.FinanceTransactions.AsNoTracking().Where(t => t.FranchiseId == fid && t.SeasonId == season.Id);
        if (Enum.TryParse<FinanceCategory>(category, true, out var c)) q = q.Where(t => t.Category == c);
        return await q.OrderByDescending(t => t.CreatedAt)
            .Select(t => new TransactionDto(t.Id, t.CreatedAt, t.Category.ToString(), t.Direction.ToString(), t.Amount, t.PurseBalanceAfter, t.Description, t.ReferenceType, t.ReferenceId))
            .ToPagedAsync(page, ct);
    }

    public async Task<TransactionDto> RecordExpenseAsync(RecordExpenseRequest req, CancellationToken ct)
    {
        me.Require(Perm.FinanceManage);
        var fid = me.RequireFranchise();
        if (req.Category is FinanceCategory.AuctionPurse or FinanceCategory.TradeConsideration)
            throw new AppValidationException("category", "Purse and trade-consideration entries are created by auctions, retentions and trades only.");
        if (string.IsNullOrWhiteSpace(req.Description)) throw new AppValidationException("description", "Describe the expense.");
        if (req.Amount <= 0) throw new AppValidationException("amount", "Enter a positive amount.");
        if (req.Amount >= ApprovalThreshold && !me.Has(Perm.FinanceApprove))
            throw new ForbiddenException($"Expenses of {Money.Format(ApprovalThreshold)} or more must be booked by someone with finance approval.");
        var season = await rules.CurrentSeasonAsync(ct);
        var t = await Ledger.PostAsync(db, fid, season.Id, req.Category, FinanceDirection.Debit, req.Amount, req.Description.Trim(), "Manual",
            req.Reference ?? "", $"manual:{fid}:{req.IdempotencyKey}", me.UserId, clock.UtcNow, ct);
        audit.Record("finance.expense_recorded", "FinanceTransaction", t.Id, new { req.Description, req.Amount, Category = req.Category.ToString() }, fid);
        await db.SaveChangesAsync(ct);
        return new TransactionDto(t.Id, t.CreatedAt, t.Category.ToString(), t.Direction.ToString(), t.Amount, t.PurseBalanceAfter, t.Description, t.ReferenceType, t.ReferenceId);
    }

    public async Task SaveBudgetAsync(SaveBudgetRequest req, CancellationToken ct)
    {
        me.Require(Perm.FinanceManage);
        var fid = me.RequireFranchise();
        if (string.IsNullOrWhiteSpace(req.Label) || req.Amount < 0) throw new AppValidationException("amount", "Enter a label and a non-negative amount.");
        var season = await rules.CurrentSeasonAsync(ct);
        var b = await db.BudgetAllocations.FirstOrDefaultAsync(x => x.FranchiseId == fid && x.SeasonId == season.Id && x.Category == req.Category && x.Label == req.Label.Trim(), ct);
        if (b is null) db.BudgetAllocations.Add(b = new BudgetAllocation { FranchiseId = fid, SeasonId = season.Id, Category = req.Category, Label = req.Label.Trim() });
        var before = b.Amount;
        b.Amount = req.Amount;
        b.UpdatedAt = clock.UtcNow;
        audit.Record("finance.budget_set", "BudgetAllocation", b.Id, new { req.Label, from = before, to = req.Amount }, fid);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Contracts ----------------

    public async Task<IReadOnlyList<ContractDto>> ContractsAsync(Guid? franchiseId, string? status, CancellationToken ct)
    {
        me.Require(Perm.ContractsRead);
        var fid = me.RequireFranchise(franchiseId);
        var q = db.PlayerContracts.AsNoTracking().Include(c => c.Payments).Where(c => c.FranchiseId == fid);
        if (Enum.TryParse<ContractStatus>(status, true, out var st)) q = q.Where(c => c.Status == st);
        var seasons = await db.Seasons.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var rows = await q.Select(c => new { C = c, Player = c.Player!.FullName, Role = c.Player.PrimaryRole!.Name }).OrderByDescending(x => x.C.AnnualSalary).ToListAsync(ct);
        return rows.Select(x => new ContractDto(x.C.Id, x.C.PlayerId, x.Player, x.Role, seasons.GetValueOrDefault(x.C.StartSeasonId, ""), seasons.GetValueOrDefault(x.C.EndSeasonId, ""),
            x.C.StartDate, x.C.EndDate, x.C.AnnualSalary, x.C.Acquisition.ToString(), x.C.Status.ToString(), x.C.Notes,
            x.C.Payments.OrderBy(p => p.DueDate).Select(p => new PaymentDto(p.Id, p.DueDate, p.Amount, p.Status.ToString(), p.PaidAt)).ToList())).ToList();
    }

    public async Task<Guid> SaveContractAsync(Guid? id, SaveContractRequest req, CancellationToken ct)
    {
        me.Require(Perm.ContractsManage);
        var fid = me.RequireFranchise();
        if (req.AnnualSalary <= 0) throw new AppValidationException("annualSalary", "Enter the annual salary.");
        if (req.EndDate <= req.StartDate) throw new AppValidationException("endDate", "The end date must be after the start date.");
        if (req.Instalments is < 1 or > 12) throw new AppValidationException("instalments", "Use 1–12 instalments.");
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PlayerId, ct) ?? throw new AppValidationException("playerId", "Unknown player.");
        if (player.CurrentFranchiseId != fid) throw new AppValidationException("playerId", "Contracts can only be created for players in your squad.");
        PlayerContract c;
        if (id is { } existing)
        {
            c = await db.PlayerContracts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Contract");
            if (c.Status != ContractStatus.Active) throw new ConflictException("Only active contracts can be changed.");
            if (c.Payments.Any(p => p.Status == PaymentStatus.Paid) && c.AnnualSalary != req.AnnualSalary)
                throw new ConflictException("The salary of a contract with paid instalments cannot be changed; terminate it and create a new one.");
        }
        else
        {
            if (await db.PlayerContracts.AnyAsync(x => x.PlayerId == req.PlayerId && x.FranchiseId == fid && x.Status == ContractStatus.Active, ct))
                throw new ConflictException("This player already has an active contract.");
            db.PlayerContracts.Add(c = new PlayerContract { PlayerId = req.PlayerId, FranchiseId = fid, Status = ContractStatus.Active });
        }
        var before = c.AnnualSalary;
        c.StartSeasonId = req.StartSeasonId;
        c.EndSeasonId = req.EndSeasonId;
        c.StartDate = req.StartDate;
        c.EndDate = req.EndDate;
        c.AnnualSalary = req.AnnualSalary;
        c.Acquisition = req.Acquisition;
        c.Notes = req.Notes?.Trim();
        // Rebuild the unpaid schedule: equal instalments spread across the contract period.
        foreach (var p in c.Payments.Where(p => p.Status == PaymentStatus.Scheduled).ToList()) { c.Payments.Remove(p); db.ContractPayments.Remove(p); }
        var paid = c.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var remaining = req.AnnualSalary - paid;
        if (remaining > 0)
        {
            var span = req.EndDate.DayNumber - req.StartDate.DayNumber;
            var each = Math.Round(remaining / req.Instalments, 2);
            for (var i = 0; i < req.Instalments; i++)
            {
                var amount = i == req.Instalments - 1 ? remaining - each * (req.Instalments - 1) : each;
                c.Payments.Add(new ContractPayment { DueDate = req.StartDate.AddDays(span * i / req.Instalments), Amount = amount, Status = PaymentStatus.Scheduled });
            }
        }
        audit.Record(id is null ? "contracts.created" : "contracts.updated", "PlayerContract", c.Id, new { player.FullName, from = before, to = req.AnnualSalary }, fid);
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task TerminateContractAsync(Guid id, string reason, CancellationToken ct)
    {
        me.Require(Perm.ContractsManage);
        if (!me.Has(Perm.FinanceApprove)) throw new ForbiddenException("Terminating a contract needs finance approval rights.");
        var c = await db.PlayerContracts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Contract");
        if (c.Status != ContractStatus.Active) throw new ConflictException("The contract is not active.");
        if (string.IsNullOrWhiteSpace(reason)) throw new AppValidationException("reason", "Give a reason.");
        c.Status = ContractStatus.Terminated;
        foreach (var p in c.Payments.Where(p => p.Status == PaymentStatus.Scheduled)) p.Status = PaymentStatus.Cancelled;
        audit.Record("contracts.terminated", "PlayerContract", id, new { reason }, c.FranchiseId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Marks an instalment paid and books it in the player-compensation ledger (never the purse).</summary>
    public async Task MarkPaidAsync(Guid paymentId, CancellationToken ct)
    {
        me.Require(Perm.FinanceManage);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var p = await db.ContractPayments.Include(x => x.Contract).ThenInclude(c => c!.Player).FirstOrDefaultAsync(x => x.Id == paymentId, ct)
                ?? throw new NotFoundException("Payment");
            me.RequireFranchiseAccess(p.Contract!.FranchiseId);
            if (p.Status != PaymentStatus.Scheduled) throw new ConflictException("This instalment is not open.");
            var season = await rules.CurrentSeasonAsync(ct);
            var t = await Ledger.PostAsync(db, p.Contract.FranchiseId, season.Id, FinanceCategory.PlayerCompensation, FinanceDirection.Debit, p.Amount,
                $"Salary instalment: {p.Contract.Player!.FullName} (due {p.DueDate:d MMM yyyy})", "ContractPayment", p.Id.ToString(), $"payment:{p.Id}", me.UserId, clock.UtcNow, ct);
            p.Status = PaymentStatus.Paid;
            p.PaidAt = clock.UtcNow;
            p.FinanceTransactionId = t.Id;
            audit.Record("finance.payment_paid", "ContractPayment", p.Id, new { p.Amount }, p.Contract.FranchiseId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Reminds finance staff about instalments due within 7 days (background job).</summary>
    public async Task<int> RemindDuePaymentsAsync(CancellationToken ct)
    {
        var soon = clock.Today.AddDays(7);
        var due = await db.ContractPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Scheduled && p.DueDate <= soon && p.Contract!.Status == ContractStatus.Active)
            .Select(p => new { p.Id, p.DueDate, p.Amount, p.Contract!.FranchiseId, Player = p.Contract.Player!.FullName }).ToListAsync(ct);
        foreach (var p in due)
            await notify.NotifyFranchiseAsync(p.FranchiseId, "finance.payment_due", $"Salary instalment due {p.DueDate:d MMM}",
                $"{p.Player}: {Money.Format(p.Amount)}.", "/finance", [Perm.FinanceManage], dedupKey: $"payment-due:{p.Id}", ct: ct);
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private async Task<List<BudgetDto>> BudgetsAsync(Guid fid, int seasonId, CancellationToken ct)
    {
        var budgets = await db.BudgetAllocations.AsNoTracking().Where(b => b.FranchiseId == fid && b.SeasonId == seasonId).ToListAsync(ct);
        var spent = await db.FinanceTransactions.AsNoTracking().Where(t => t.FranchiseId == fid && t.SeasonId == seasonId && t.Direction == FinanceDirection.Debit)
            .GroupBy(t => t.Category).Select(g => new { g.Key, Total = g.Sum(t => t.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        return budgets.OrderBy(b => b.Category).ThenBy(b => b.Label)
            .Select(b => new BudgetDto(b.Id, b.Category.ToString(), b.Label, b.Amount, spent.GetValueOrDefault(b.Category))).ToList();
    }
}

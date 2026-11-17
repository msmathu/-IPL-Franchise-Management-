using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Finance;

/// <summary>
/// The only way money moves. AuctionPurse postings also change FranchiseSeason.PurseRemaining (protected by its
/// row version); other categories are separate ledgers that never touch the purse. Call inside a DB transaction.
/// </summary>
public static class Ledger
{
    public static async Task<FinanceTransaction> PostAsync(
        IAppDbContext db, Guid franchiseId, int seasonId, FinanceCategory category, FinanceDirection direction, decimal amount,
        string description, string referenceType, string referenceId, string idempotencyKey, Guid? userId, DateTime now,
        CancellationToken ct, bool allowNegativePurse = false)
    {
        if (amount <= 0) throw new AppValidationException("amount", "Amounts must be positive.");
        // IgnoreQueryFilters: posting for the other party of a trade must work regardless of the caller's tenant.
        if (await db.FinanceTransactions.IgnoreQueryFilters().AnyAsync(t => t.IdempotencyKey == idempotencyKey, ct))
            throw new ConflictException("This transaction has already been recorded.");

        decimal? balance = null;
        if (category == FinanceCategory.AuctionPurse)
        {
            var fs = await db.FranchiseSeasons.FirstOrDefaultAsync(s => s.FranchiseId == franchiseId && s.SeasonId == seasonId, ct)
                ?? throw new DomainException("finance.no_purse", "This franchise has no purse for the season.");
            var next = direction == FinanceDirection.Credit ? fs.PurseRemaining + amount : fs.PurseRemaining - amount;
            if (next < 0 && !allowNegativePurse)
                throw new DomainException("finance.insufficient_purse", $"Not enough purse: {Money.Format(fs.PurseRemaining)} left, {Money.Format(amount)} needed.");
            fs.PurseRemaining = next;
            balance = next;
        }
        var tx = new FinanceTransaction
        {
            FranchiseId = franchiseId, SeasonId = seasonId, Category = category, Direction = direction, Amount = amount,
            PurseBalanceAfter = balance, Description = description, ReferenceType = referenceType, ReferenceId = referenceId,
            IdempotencyKey = idempotencyKey, CreatedAt = now, CreatedBy = userId,
        };
        db.FinanceTransactions.Add(tx);
        return tx;
    }
}

using Ipl.Application.Analytics;
using Ipl.Application.Rules;
using Ipl.Application.Squads;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Application.Auctions;

public record AssistantAlternativeDto(Guid PlayerId, string Name, string Role, bool IsOverseas, decimal BasePrice, decimal ModelMid, string SetName);

public record AuctionAdviceDto(
    Guid LotId, Guid PlayerId, string PlayerName, string Verdict, string Headline, decimal? NextBid, decimal? StopAt, decimal ModelLow, decimal ModelMid,
    decimal ModelHigh, decimal Rating, string DataConfidence, int FitScore, IReadOnlyList<string> Reasons, IReadOnlyList<string> Cautions,
    int RivalsAbleToBid, decimal PurseRemaining, decimal MaxBidAllowed, int SlotsRemaining, int OverseasSlotsRemaining,
    IReadOnlyList<AssistantAlternativeDto> Alternatives, string Disclaimer);

/// <summary>
/// Live bid advice for the player on the block, for the signed-in franchise: how far to go and why. It combines the
/// valuation model (rating from real statistics), the squad's gaps, the purse and squad rules, rival purses and the
/// remaining pool. It is an explainable model, not a guarantee; the decision stays with the auction team.
/// </summary>
public class AuctionAssistantService(IAppDbContext db, ICurrentUser me, IClock clock, IRulesService rules, AuctionService auctions, ValuationService valuations, RatingService ratings)
{
    public async Task<AuctionAdviceDto?> AdviseAsync(Guid auctionId, CancellationToken ct)
    {
        me.Require(Perm.ValuationRead);
        var fid = me.RequireFranchise();
        var live = await auctions.LiveAsync(auctionId, ct);
        var lot = live.CurrentLot;
        if (lot is null) return null;

        var season = await rules.CurrentSeasonAsync(ct);
        var r = await rules.ForSeasonAsync(season.Id, ct);
        var squad = await SquadMath.LoadAsync(db, fid, season.Id, clock.Today, ct);
        var comp = SquadMath.Compose(squad, r);
        var needs = RecommendationService.Needs(comp, squad, r).Select(n => n.Need).ToHashSet();
        var mine = live.Franchises.First(f => f.FranchiseId == fid);
        var value = (await valuations.RunManyAsync([lot.PlayerId], ct)).First();
        var rating = await ratings.RateAsync(lot.PlayerId, ct);
        var player = await db.Players.AsNoTracking().Where(p => p.Id == lot.PlayerId)
            .Select(p => new { Role = p.PrimaryRole!.Code, p.IsOverseas, p.IsWicketKeeper, p.BowlingType, p.IsCapped }).FirstAsync(ct);

        // How well the player fits this squad.
        List<string> reasons = [], cautions = [];
        var fit = 0;
        if (needs.Contains("Wicketkeeper") && player.IsWicketKeeper) { fit += 3; reasons.Add("Fills your wicketkeeper gap."); }
        if (needs.Contains("Bowling depth") && SquadMath.IsBowlingOption(player.Role, player.BowlingType)) { fit += 2; reasons.Add("Adds a bowling option you need."); }
        if (needs.Contains("Spin") && player.BowlingType is BowlingType.OffSpin or BowlingType.LegSpin or BowlingType.LeftArmOrthodox or BowlingType.LeftArmWristSpin)
        { fit += 2; reasons.Add("Adds spin, a gap in your squad."); }
        if (needs.Contains("Pace") && player.BowlingType is BowlingType.Fast or BowlingType.MediumFast) { fit += 2; reasons.Add("Adds pace, a gap in your squad."); }
        if (needs.Contains("Batting depth") && player.Role is "batter" or "wk_batter" or "allrounder") { fit += 1; reasons.Add("Adds batting depth."); }
        if (value.Rating >= 70) { fit += 2; reasons.Add($"High model rating ({value.Rating:0}/100)."); }
        else if (value.Rating >= 60) { fit += 1; reasons.Add($"Good model rating ({value.Rating:0}/100)."); }
        if (fit == 0) reasons.Add("No specific squad gap filled: a depth signing at best.");
        if (rating.Confidence is "None" or "Low") cautions.Add(rating.Confidence == "None"
            ? "No match statistics for this player: the value is a neutral placeholder."
            : "Few matches on record: the estimate is uncertain.");
        if (player.IsOverseas && mine.OverseasSlotsRemaining <= 1) cautions.Add($"Uses one of your last overseas slots ({mine.OverseasSlotsRemaining} left).");

        // How far to go: model value adjusted for fit, never above what the rules allow.
        var target = fit >= 4 ? value.High : fit >= 2 ? value.Mid : value.Low;
        var minBase = r.Decimal(RuleKeys.MinBasePrice);
        if (comp.SlotsRemaining > 1)
        {
            // Keep enough for the rest of the squad at an average of the open slots (not just the minimum).
            var share = mine.PurseRemaining / comp.SlotsRemaining * (fit >= 4 ? 3m : fit >= 2 ? 2m : 1.2m);
            if (target > share) { target = share; cautions.Add($"Stop price limited to keep purse for {comp.SlotsRemaining - 1} more open slots."); }
        }
        var stopAt = Math.Min(target, mine.MaxBidAllowed);
        stopAt = Math.Max(stopAt, lot.BasePrice);
        stopAt = Math.Floor(stopAt / (5 * Money.Lakh)) * (5 * Money.Lakh);
        var next = live.Me?.NextBid ?? live.NextBid;
        var rivals = live.Franchises.Count(f => f.FranchiseId != fid && f.SlotsRemaining > 0 && f.MaxBidAllowed >= (next ?? lot.BasePrice)
            && (!lot.IsOverseas || f.OverseasSlotsRemaining > 0));
        if (rivals >= 5 && fit >= 2) cautions.Add($"{rivals} other teams can still afford this bid: expect competition.");

        string verdict, headline;
        if (live.Me is { CanBid: false } m && lot.CurrentBidFranchiseId != fid)
        { verdict = "CannotBid"; headline = m.CannotBidReason ?? "You cannot bid on this player."; }
        else if (lot.CurrentBidFranchiseId == fid)
        { verdict = "Leading"; headline = $"You hold the highest bid. Your stop price is {Money.Format(stopAt)}."; }
        else if (next is null || next <= stopAt)
        { verdict = fit >= 2 ? "Bid" : "Consider"; headline = fit >= 2 ? $"Bid up to {Money.Format(stopAt)}." : $"Only if cheap: stop at {Money.Format(stopAt)}."; }
        else
        { verdict = "Pass"; headline = $"Let him go: the next bid ({Money.Format(next.Value)}) is above your stop price ({Money.Format(stopAt)})."; }

        // Cheaper options with the same role still to come.
        var pending = await db.AuctionPlayers.AsNoTracking()
            .Where(l => l.AuctionEventId == auctionId && l.Status == AuctionLotStatus.Pending && l.Id != lot.Id && l.Player!.PrimaryRole!.Code == player.Role
                && (!l.Player.IsOverseas || mine.OverseasSlotsRemaining > 0))
            .Select(l => new { l.PlayerId, l.Player!.FullName, Role = l.Player.PrimaryRole!.Name, l.Player.IsOverseas, l.BasePrice, l.SetName })
            .Take(40).ToListAsync(ct);
        var altVals = pending.Count == 0 ? [] : (await valuations.RunManyAsync(pending.Select(p => p.PlayerId).ToList(), ct)).ToDictionary(v => v.PlayerId);
        var alternatives = pending.Where(p => altVals.ContainsKey(p.PlayerId))
            .OrderByDescending(p => altVals[p.PlayerId].Rating).Take(3)
            .Select(p => new AssistantAlternativeDto(p.PlayerId, p.FullName, p.Role, p.IsOverseas, p.BasePrice, altVals[p.PlayerId].Mid, p.SetName)).ToList();
        if (verdict == "Pass" && alternatives.Count > 0) reasons.Add($"Similar options later in this auction: {string.Join(", ", alternatives.Select(a => a.Name))}.");

        return new AuctionAdviceDto(lot.Id, lot.PlayerId, lot.PlayerName, verdict, headline, next, stopAt, value.Low, value.Mid, value.High, value.Rating,
            rating.Confidence, fit, reasons, cautions, rivals, mine.PurseRemaining, mine.MaxBidAllowed, comp.SlotsRemaining, mine.OverseasSlotsRemaining,
            alternatives, "Model-based advice from statistics, squad needs and purse rules. A guide only: the auction team decides.");
    }
}

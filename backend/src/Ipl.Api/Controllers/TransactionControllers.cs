using Ipl.Api.Infrastructure;
using Ipl.Application;
using Ipl.Application.Analytics;
using Ipl.Application.Auctions;
using Ipl.Application.Squads;
using Ipl.Application.Trading;
using Ipl.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ipl.Api.Controllers;

public record MessageOnlyRequest(string? Message);
public record TradeValueRequest(IReadOnlyList<Guid> Gives, IReadOnlyList<Guid> Receives, decimal CashPaid, decimal CashReceived);

[Route("api/v{version:apiVersion}/trades")]
public class TradesController(TradingService trades, RecommendationService recommendations) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.TradesRead)]
    public Task<PagedResult<TradeSummaryDto>> List([FromQuery] string? status, [FromQuery] Guid? franchiseId, [FromQuery(Name = "")] PageRequest page, CancellationToken ct) =>
        trades.ListAsync(status, franchiseId, page, ct);

    [HttpGet("{id:guid}"), HasPermission(Perm.TradesRead)]
    public Task<TradeDetailDto> Get(Guid id, CancellationToken ct) => trades.GetAsync(id, ct);

    [HttpPost, HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Create(CreateTradeRequest req, CancellationToken ct) => trades.CreateAsync(req, ct);

    [HttpPut("{id:guid}"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> UpdateDraft(Guid id, TradeTermsRequest req, CancellationToken ct) => trades.UpdateDraftAsync(id, req, ct);

    [HttpPost("{id:guid}/submit"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Submit(Guid id, CancellationToken ct) => trades.SubmitAsync(id, ct);

    [HttpPost("{id:guid}/counter"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Counter(Guid id, CounterTradeRequest req, CancellationToken ct) => trades.CounterAsync(id, req, ct);

    [HttpPost("{id:guid}/accept"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Accept(Guid id, MessageOnlyRequest req, CancellationToken ct) => trades.AcceptAsync(id, req.Message, ct);

    [HttpPost("{id:guid}/reject"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Reject(Guid id, MessageOnlyRequest req, CancellationToken ct) => trades.RejectAsync(id, req.Message, ct);

    [HttpPost("{id:guid}/withdraw"), HasPermission(Perm.TradesCreate)]
    public Task<TradeDetailDto> Withdraw(Guid id, MessageOnlyRequest req, CancellationToken ct) => trades.WithdrawAsync(id, req.Message, ct);

    [HttpPost("{id:guid}/messages"), HasPermission(Perm.TradesRead)]
    public Task<TradeDetailDto> Message(Guid id, TradeMessageRequest req, CancellationToken ct) => trades.AddMessageAsync(id, req, ct);

    [HttpPost("{id:guid}/approvals/{approvalId:guid}")]
    public Task<TradeDetailDto> Decide(Guid id, Guid approvalId, ApprovalDecisionRequest req, CancellationToken ct) => trades.DecideApprovalAsync(id, approvalId, req, ct);

    [HttpPost("{id:guid}/complete")]
    public Task<TradeDetailDto> Complete(Guid id, CancellationToken ct) => trades.CompleteAsync(id, ct);

    [HttpGet("{id:guid}/impact"), HasPermission(Perm.TradesRead)]
    public Task<TradeImpactDto> Impact(Guid id, CancellationToken ct) => trades.ImpactForTradeAsync(id, ct);

    [HttpPost("impact"), HasPermission(Perm.TradesRead)]
    public Task<TradeImpactDto> PreviewImpact(TradeTermsRequest req, CancellationToken ct) => trades.PreviewImpactAsync(req, ct);

    [HttpPost("value"), HasPermission(Perm.ValuationRead)]
    public Task<TradeValueDto> Value(TradeValueRequest req, CancellationToken ct) => recommendations.TradeValueAsync(req.Gives, req.Receives, req.CashPaid, req.CashReceived, ct);
}

public record SimulateRequest(IReadOnlyList<Guid> PlayerIds);
public record ReleaseRequest(Guid PlayerId, string? Reason);

[Route("api/v{version:apiVersion}/retention")]
public class RetentionController(RetentionService retention) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.RetentionRead)]
    public Task<RetentionOverviewDto> Overview([FromQuery] Guid? franchiseId, CancellationToken ct) => retention.OverviewAsync(franchiseId, ct);

    [HttpPost("simulate"), HasPermission(Perm.RetentionRead)]
    public Task<RetentionSimulationDto> Simulate(SimulateRequest req, [FromQuery] Guid? franchiseId, CancellationToken ct) => retention.SimulateAsync(req.PlayerIds, franchiseId, ct);

    [HttpPost("plans"), HasPermission(Perm.RetentionManage)]
    public async Task<object> SavePlan(SavePlanRequest req, CancellationToken ct) => new { id = await retention.SavePlanAsync(req, ct) };

    [HttpDelete("plans/{id:guid}"), HasPermission(Perm.RetentionManage)]
    public async Task<IActionResult> DeletePlan(Guid id, CancellationToken ct) { await retention.DeletePlanAsync(id, ct); return NoContent(); }

    [HttpPost("plans/{id:guid}/submit"), HasPermission(Perm.RetentionSubmit)]
    public Task<RetentionSimulationDto> Submit(Guid id, CancellationToken ct) => retention.SubmitAsync(id, ct);

    [HttpPost("release"), HasPermission(Perm.RetentionManage)]
    public async Task<IActionResult> Release(ReleaseRequest req, CancellationToken ct) { await retention.ReleaseAsync(req.PlayerId, req.Reason, ct); return NoContent(); }

    [HttpGet("history"), HasPermission(Perm.RetentionRead)]
    public Task<IReadOnlyList<RetentionHistoryDto>> History([FromQuery] Guid? franchiseId, CancellationToken ct) => retention.HistoryAsync(franchiseId, ct);
}

[Route("api/v{version:apiVersion}/squad")]
public class SquadController(SquadService squads, ICurrentUser me) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.SquadRead)]
    public Task<SquadDto> Mine(CancellationToken ct) => squads.GetAsync(me.RequireFranchise(), ct);

    [HttpGet("playing-xi"), HasPermission(Perm.SquadRead)]
    public Task<IReadOnlyList<PlayingXIDto>> XIs(CancellationToken ct) => squads.ListXIsAsync(ct);

    [HttpPost("playing-xi/validate"), HasPermission(Perm.SquadRead)]
    public Task<XIValidationDto> Validate(SavePlayingXIRequest req, CancellationToken ct) => squads.ValidateAsync(req, ct);

    [HttpPost("playing-xi"), HasPermission(Perm.PlayingXIManage)]
    public async Task<object> Create(SavePlayingXIRequest req, CancellationToken ct) => new { id = await squads.SaveXIAsync(null, req, ct) };

    [HttpPut("playing-xi/{id:guid}"), HasPermission(Perm.PlayingXIManage)]
    public async Task<object> Update(Guid id, SavePlayingXIRequest req, CancellationToken ct) => new { id = await squads.SaveXIAsync(id, req, ct) };

    [HttpDelete("playing-xi/{id:guid}"), HasPermission(Perm.PlayingXIManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await squads.DeleteXIAsync(id, ct); return NoContent(); }
}

public record ImportEligibleRequest(decimal BasePrice);
public record OpenLotRequest(Guid? LotId);
public record CloseLotRequest(bool Sold);
public record ReopenRequest(IReadOnlyList<Guid>? LotIds);

[Route("api/v{version:apiVersion}/auctions")]
public class AuctionsController(AuctionService auctions) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.AuctionsRead)]
    public Task<IReadOnlyList<AuctionSummaryDto>> List(CancellationToken ct) => auctions.ListAsync(ct);

    [HttpPost, HasPermission(Perm.AuctionsManage)]
    public async Task<object> Create(CreateAuctionRequest req, CancellationToken ct) => new { id = await auctions.CreateAsync(req, ct) };

    [HttpPut("{id:guid}"), HasPermission(Perm.AuctionsManage)]
    public async Task<IActionResult> Update(Guid id, CreateAuctionRequest req, CancellationToken ct) { await auctions.UpdateAsync(id, req, ct); return NoContent(); }

    [HttpGet("{id:guid}/lots"), HasPermission(Perm.AuctionsRead)]
    public Task<IReadOnlyList<LotDto>> Lots(Guid id, CancellationToken ct) => auctions.LotsAsync(id, ct);

    [HttpPost("{id:guid}/lots"), HasPermission(Perm.AuctionsManage)]
    public async Task<object> AddLots(Guid id, AddLotsRequest req, CancellationToken ct) => new { added = await auctions.AddLotsAsync(id, req, ct) };

    [HttpPost("{id:guid}/lots/import-eligible"), HasPermission(Perm.AuctionsManage)]
    public async Task<object> ImportEligible(Guid id, ImportEligibleRequest req, CancellationToken ct) => new { added = await auctions.ImportEligibleAsync(id, req.BasePrice, ct) };

    [HttpDelete("{id:guid}/lots/{lotId:guid}"), HasPermission(Perm.AuctionsManage)]
    public async Task<IActionResult> RemoveLot(Guid id, Guid lotId, CancellationToken ct) { await auctions.RemoveLotAsync(id, lotId, ct); return NoContent(); }

    [HttpPost("{id:guid}/{op:regex(^(ready|start|pause|resume|complete)$)}"), HasPermission(Perm.AuctionsManage)]
    public Task<LiveAuctionDto> SetStatus(Guid id, string op, CancellationToken ct) => auctions.SetStatusAsync(id, op, ct);

    [HttpPost("{id:guid}/open-lot"), HasPermission(Perm.AuctionsManage)]
    public Task<LiveAuctionDto> OpenLot(Guid id, OpenLotRequest req, CancellationToken ct) => auctions.OpenLotAsync(id, req.LotId, ct);

    [HttpPost("{id:guid}/bids"), HasPermission(Perm.AuctionsBid), EnableRateLimiting("bids")]
    public Task<LiveAuctionDto> Bid(Guid id, PlaceBidRequest req, CancellationToken ct) => auctions.BidAsync(id, req, ct);

    [HttpPost("{id:guid}/close-lot"), HasPermission(Perm.AuctionsManage)]
    public Task<LiveAuctionDto> CloseLot(Guid id, CloseLotRequest req, CancellationToken ct) => auctions.CloseLotAsync(id, req.Sold, ct);

    [HttpPost("{id:guid}/reopen-unsold"), HasPermission(Perm.AuctionsManage)]
    public async Task<object> Reopen(Guid id, ReopenRequest req, CancellationToken ct) => new { reopened = await auctions.ReopenUnsoldAsync(id, req.LotIds, ct) };

    [HttpGet("{id:guid}/live"), HasPermission(Perm.AuctionsRead)]
    public Task<LiveAuctionDto> Live(Guid id, CancellationToken ct) => auctions.LiveAsync(id, ct);

    [HttpGet("{id:guid}/results"), HasPermission(Perm.AuctionsRead)]
    public Task<AuctionResultsDto> Results(Guid id, CancellationToken ct) => auctions.ResultsAsync(id, ct);
}

[Route("api/v{version:apiVersion}/replacements")]
public class ReplacementsController(ReplacementService replacements) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.SquadRead)]
    public Task<IReadOnlyList<ReplacementRequestDto>> List(CancellationToken ct) => replacements.ListAsync(ct);

    [HttpGet("rules"), HasPermission(Perm.SquadRead)]
    public Task<ReplacementRulesDto> Rules(CancellationToken ct) => replacements.RulesAsync(ct);

    [HttpPost, HasPermission(Perm.SquadManage)]
    public async Task<object> Create(CreateReplacementRequest req, CancellationToken ct) => new { id = await replacements.CreateAsync(req, ct) };

    [HttpPost("{id:guid}/cancel"), HasPermission(Perm.SquadManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await replacements.CancelAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/approve"), HasPermission(Perm.TradesLeagueApprove)]
    public async Task<IActionResult> Approve(Guid id, ReplacementDecision req, CancellationToken ct) { await replacements.ApproveAsync(id, req.Note, ct); return NoContent(); }

    [HttpPost("{id:guid}/reject"), HasPermission(Perm.TradesLeagueApprove)]
    public async Task<IActionResult> Reject(Guid id, ReplacementDecision req, CancellationToken ct) { await replacements.RejectAsync(id, req.Note, ct); return NoContent(); }
}

[Route("api/v{version:apiVersion}/auctions")]
public class AuctionAssistantController(AuctionAssistantService assistant, Ipl.Infrastructure.Services.AuctionListImporter lists) : ApiControllerBase
{
    /// <summary>Imports the official auction list (CSV or Excel): lots, sets, base prices; creates missing players.</summary>
    [HttpPost("{id:guid}/import-list"), HasPermission(Perm.AuctionsManage), RequestSizeLimit(10_000_000)]
    public async Task<Ipl.Infrastructure.Services.AuctionListImportResult> ImportList(Guid id, IFormFile file, [FromForm] string source, [FromForm] bool replaceProvisional, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        var copy = new MemoryStream();
        await s.CopyToAsync(copy, ct);
        copy.Position = 0;
        return await lists.ImportAsync(id, copy, file.FileName, source, replaceProvisional, ct);
    }

    /// <summary>Bid advice for the player on the block, for the signed-in franchise (model-based).</summary>
    [HttpGet("{id:guid}/assistant"), HasPermission(Perm.ValuationRead)]
    public Task<AuctionAdviceDto?> Advice(Guid id, CancellationToken ct) => assistant.AdviseAsync(id, ct);
}

[Route("api/v{version:apiVersion}/league/official-squads")]
public class OfficialSquadsController(OfficialSquadService official) : ApiControllerBase
{
    /// <summary>Applies the league's official squad announcement (trades, retained lists, purses) for the current season.</summary>
    [HttpPost, HasPermission(Perm.SeasonsManage)]
    public Task<OfficialSquadUpdateResult> Apply(OfficialSquadUpdateRequest req, CancellationToken ct) => official.ApplyAsync(req, ct);
}

using Ipl.Api.Infrastructure;
using Ipl.Application;
using Ipl.Application.Analytics;
using Ipl.Application.Franchises;
using Ipl.Application.Players;
using Ipl.Application.Squads;
using Ipl.Application.Statistics;
using Ipl.Domain;
using Ipl.Infrastructure.Persistence;
using Ipl.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ipl.Api.Controllers;

[Route("api/v{version:apiVersion}/franchises")]
public class FranchisesController(FranchiseService franchises, SquadService squads) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.FranchisesRead)]
    public Task<IReadOnlyList<FranchiseListItemDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => franchises.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}"), HasPermission(Perm.FranchisesRead)]
    public Task<FranchiseDetailDto> Get(Guid id, CancellationToken ct) => franchises.GetAsync(id, ct);

    [HttpGet("{id:guid}/squad"), HasPermission(Perm.SquadRead)]
    public Task<SquadDto> Squad(Guid id, CancellationToken ct) => squads.GetAsync(id, ct);

    [HttpGet("{id:guid}/activity"), HasPermission(Perm.AuditRead)]
    public Task<PagedResult<FranchiseActivityDto>> Activity(Guid id, [FromQuery] PageRequest page, CancellationToken ct) => franchises.ActivityAsync(id, page, ct);

    [HttpPost, HasPermission(Perm.FranchisesManage)]
    public async Task<object> Create(SaveFranchiseRequest req, CancellationToken ct) => new { id = await franchises.CreateAsync(req, ct) };

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveFranchiseRequest req, CancellationToken ct) { await franchises.UpdateAsync(id, req, ct); return NoContent(); }

    [HttpPost("{id:guid}/activate"), HasPermission(Perm.FranchisesManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) { await franchises.SetActiveAsync(id, true, ct); return NoContent(); }

    [HttpPost("{id:guid}/deactivate"), HasPermission(Perm.FranchisesManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) { await franchises.SetActiveAsync(id, false, ct); return NoContent(); }

    [HttpPost("{id:guid}/staff")]
    public async Task<object> AddStaff(Guid id, SaveStaffRequest req, CancellationToken ct) => new { id = await franchises.AddStaffAsync(id, req, ct) };

    [HttpDelete("{id:guid}/staff/{staffId:guid}")]
    public async Task<IActionResult> RemoveStaff(Guid id, Guid staffId, CancellationToken ct) { await franchises.RemoveStaffAsync(id, staffId, ct); return NoContent(); }
}

public record AvailabilityRequest(AvailabilityStatus Status, string? PublicNote, DateOnly? ExpectedReturn);

[Route("api/v{version:apiVersion}/players")]
public class PlayersController(PlayerService players, StatisticsService stats, RatingService ratings, ValuationService valuations) : ApiControllerBase
{
    [HttpGet("lookups"), HasPermission(Perm.PlayersRead)]
    public Task<object> Lookups(CancellationToken ct) => players.LookupsAsync(ct);

    [HttpGet, HasPermission(Perm.PlayersRead)]
    public Task<PagedResult<PlayerListItemDto>> Search([FromQuery] PlayerSearchRequest filter, [FromQuery] PageRequest page, CancellationToken ct) =>
        players.SearchAsync(filter, page, ct);

    [HttpGet("{id:guid}"), HasPermission(Perm.PlayersRead)]
    public Task<PlayerDetailDto> Get(Guid id, CancellationToken ct) => players.GetAsync(id, ct);

    [HttpPost, HasPermission(Perm.PlayersManage)]
    public async Task<object> Create(SavePlayerRequest req, CancellationToken ct) => new { id = await players.CreateAsync(req, ct) };

    [HttpPut("{id:guid}"), HasPermission(Perm.PlayersManage)]
    public async Task<IActionResult> Update(Guid id, SavePlayerRequest req, CancellationToken ct) { await players.UpdateAsync(id, req, ct); return NoContent(); }

    [HttpDelete("{id:guid}"), HasPermission(Perm.PlayersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await players.DeleteAsync(id, ct); return NoContent(); }

    [HttpPost("import"), HasPermission(Perm.PlayersManage), RequestSizeLimit(5_000_000)]
    public async Task<ImportResult> Import(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await players.ImportCsvAsync(s, ct);
    }

    /// <summary>Status of the free-photo import (Wikimedia Commons).</summary>
    [HttpGet("photos"), HasPermission(Perm.PlayersRead)]
    public Task<PhotoStatusDto> PhotoStatus([FromServices] PhotoImportRunner runner, [FromServices] AppDbContext db, CancellationToken ct) => runner.StatusAsync(db, ct);

    /// <summary>Downloads freely licensed player photos from Wikimedia Commons in the background.</summary>
    [HttpPost("photos/import"), HasPermission(Perm.PlayersManage)]
    public async Task<IActionResult> PhotoImport([FromServices] PhotoImportRunner runner, [FromServices] AppDbContext db, CancellationToken ct)
    {
        if (!runner.TryStart()) throw new ConflictException("A photo import is already running.");
        return Accepted(await runner.StatusAsync(db, ct));
    }

    [HttpPut("{id:guid}/cricsheet-id"), HasPermission(Perm.StatsImport)]
    public async Task<IActionResult> SetCricsheetId(Guid id, CricsheetLinkRequest req, CancellationToken ct)
    {
        await players.SetCricsheetIdAsync(id, req.CricsheetId, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/availability"), HasPermission(Perm.AvailabilityManage)]
    public async Task<IActionResult> SetAvailability(Guid id, AvailabilityRequest req, CancellationToken ct)
    {
        await players.SetAvailabilityAsync(id, req.Status, req.PublicNote, req.ExpectedReturn, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/statistics"), HasPermission(Perm.StatsRead)]
    public Task<PlayerStatisticsDto> Statistics(Guid id, CancellationToken ct) => stats.GetAsync(id, ct);

    [HttpGet("{id:guid}/analysis"), HasPermission(Perm.StatsRead)]
    public Task<PerformanceAnalysisDto> Analysis(Guid id, [FromQuery] int? competitionId, CancellationToken ct) => stats.AnalyseAsync(id, competitionId, ct);

    [HttpGet("{id:guid}/rating"), HasPermission(Perm.PlayersRead)]
    public Task<PlayerRatingDto> Rating(Guid id, CancellationToken ct) => ratings.RateAsync(id, ct);

    [HttpGet("{id:guid}/valuation"), HasPermission(Perm.ValuationRead)]
    public Task<ValuationDto> Valuation(Guid id, CancellationToken ct) => valuations.LatestAsync(id, ct);

    [HttpPost("{id:guid}/valuation"), HasPermission(Perm.ValuationRun)]
    public Task<ValuationDto> RunValuation(Guid id, CancellationToken ct) => valuations.RunAsync(id, ct);
}

[Route("api/v{version:apiVersion}/statistics")]
public class StatisticsController(StatisticsService stats, CricsheetImportRunner cricsheet, AppDbContext db, IAuditService audit) : ApiControllerBase
{
    /// <summary>Status of the Cricsheet ball-by-ball import (last run, players without a link, licence).</summary>
    [HttpGet("cricsheet"), HasPermission(Perm.StatsRead)]
    public Task<CricsheetStatusDto> CricsheetStatus(CancellationToken ct) => cricsheet.StatusAsync(db, ct);

    /// <summary>Starts a Cricsheet import in the background (one at a time).</summary>
    [HttpPost("cricsheet/import"), HasPermission(Perm.StatsImport)]
    public async Task<IActionResult> CricsheetImport(CancellationToken ct)
    {
        if (!cricsheet.TryStart())
            throw new ConflictException("A Cricsheet import is already running.");
        audit.Record("stats.cricsheet-started", "Statistics", null);
        await db.SaveChangesAsync(ct);
        return Accepted(await cricsheet.StatusAsync(db, ct));
    }

    [HttpPost("import"), HasPermission(Perm.StatsImport), RequestSizeLimit(10_000_000)]
    public async Task<SeasonStatImportResult> Import(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await stats.ImportSeasonCsvAsync(s, ct);
    }

    [HttpPost("recalculate"), HasPermission(Perm.StatsImport)]
    public async Task<IActionResult> Recalculate(CancellationToken ct) { await stats.RecalculateAsync(null, ct); return NoContent(); }
}

public record CricsheetLinkRequest(string? CricsheetId);

public record CompareRequest(IReadOnlyList<Guid> PlayerIds);

[Route("api/v{version:apiVersion}/comparisons")]
public class ComparisonsController(ComparisonService comparisons) : ApiControllerBase
{
    [HttpPost("compare"), HasPermission(Perm.ComparisonsRead)]
    public Task<ComparisonResultDto> Compare(CompareRequest req, CancellationToken ct) => comparisons.CompareAsync(req.PlayerIds, ct);

    [HttpGet, HasPermission(Perm.ComparisonsRead)]
    public Task<IReadOnlyList<SavedComparisonDto>> Saved(CancellationToken ct) => comparisons.SavedAsync(ct);

    [HttpPost, HasPermission(Perm.ComparisonsManage)]
    public async Task<object> Save(SaveComparisonRequest req, CancellationToken ct) => new { id = await comparisons.SaveAsync(req, ct) };

    [HttpDelete("{id:guid}"), HasPermission(Perm.ComparisonsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await comparisons.DeleteAsync(id, ct); return NoContent(); }
}

[Route("api/v{version:apiVersion}/valuation")]
public class ValuationController(RecommendationService recommendations) : ApiControllerBase
{
    [HttpGet("strategy"), HasPermission(Perm.ValuationRead)]
    public Task<StrategyDto> Strategy([FromQuery] Guid? franchiseId, CancellationToken ct) => recommendations.AuctionStrategyAsync(franchiseId, ct);

    [HttpGet("undervalued"), HasPermission(Perm.ValuationRead)]
    public Task<IReadOnlyList<ValueGapDto>> Undervalued(CancellationToken ct) => recommendations.ValueGapsAsync(true, ct);

    [HttpGet("overvalued"), HasPermission(Perm.ValuationRead)]
    public Task<IReadOnlyList<ValueGapDto>> Overvalued(CancellationToken ct) => recommendations.ValueGapsAsync(false, ct);

    [HttpGet("squad-needs"), HasPermission(Perm.SquadRead)]
    public Task<IReadOnlyList<SquadNeedDto>> SquadNeeds([FromQuery] Guid? franchiseId, CancellationToken ct) => recommendations.SquadNeedsAsync(franchiseId, ct);
}

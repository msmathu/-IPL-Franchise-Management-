using Ipl.Api.Infrastructure;
using Ipl.Application;
using Ipl.Application.Analytics;
using Ipl.Application.Finance;
using Ipl.Application.Operations;
using Ipl.Application.Reports;
using Ipl.Application.Scouting;
using Ipl.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Ipl.Api.Controllers;

public record ReviewRequest(string? Comment);

[Route("api/v{version:apiVersion}/scouting")]
public class ScoutingController(ScoutingService scouting) : ApiControllerBase
{
    [HttpGet("scouts"), HasPermission(Perm.ScoutingManage)]
    public Task<IReadOnlyList<ScoutDto>> Scouts(CancellationToken ct) => scouting.ScoutsAsync(ct);

    [HttpGet("assignments")]
    public Task<IReadOnlyList<AssignmentDto>> Assignments([FromQuery] bool mineOnly, CancellationToken ct) => scouting.AssignmentsAsync(mineOnly, ct);

    [HttpPost("assignments"), HasPermission(Perm.ScoutingManage)]
    public async Task<object> CreateAssignment(SaveAssignmentRequest req, CancellationToken ct) => new { id = await scouting.SaveAssignmentAsync(null, req, ct) };

    [HttpPut("assignments/{id:guid}"), HasPermission(Perm.ScoutingManage)]
    public async Task<object> UpdateAssignment(Guid id, SaveAssignmentRequest req, CancellationToken ct) => new { id = await scouting.SaveAssignmentAsync(id, req, ct) };

    [HttpGet("reports")]
    public Task<PagedResult<ReportListItemDto>> Reports([FromQuery] Guid? playerId, [FromQuery] string? status, [FromQuery] PageRequest page, CancellationToken ct) =>
        scouting.ReportsAsync(playerId, status, page, ct);

    [HttpGet("reports/{id:guid}")]
    public Task<ReportDto> Report(Guid id, CancellationToken ct) => scouting.GetReportAsync(id, ct);

    [HttpPost("reports"), HasPermission(Perm.ScoutingReport)]
    public async Task<object> CreateReport(SaveReportRequest req, CancellationToken ct) => new { id = await scouting.SaveReportAsync(null, req, ct) };

    [HttpPut("reports/{id:guid}"), HasPermission(Perm.ScoutingReport)]
    public async Task<object> UpdateReport(Guid id, SaveReportRequest req, CancellationToken ct) => new { id = await scouting.SaveReportAsync(id, req, ct) };

    [HttpPost("reports/{id:guid}/review"), HasPermission(Perm.ScoutingManage)]
    public async Task<IActionResult> Review(Guid id, ReviewRequest req, CancellationToken ct) { await scouting.ReviewAsync(id, req.Comment, ct); return NoContent(); }

    [HttpDelete("reports/{id:guid}"), HasPermission(Perm.ScoutingReport)]
    public async Task<IActionResult> DeleteReport(Guid id, CancellationToken ct) { await scouting.DeleteReportAsync(id, ct); return NoContent(); }

    [HttpPost("reports/{id:guid}/documents"), HasPermission(Perm.ScoutingReport), RequestSizeLimit(6_000_000)]
    public async Task<DocumentDto> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await scouting.UploadAsync(id, file.FileName, file.ContentType, s, ct);
    }

    [HttpGet("reports/{id:guid}/documents/{documentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid documentId, CancellationToken ct)
    {
        var (name, type, content) = await scouting.DownloadAsync(id, documentId, ct);
        return File(content, type, name);
    }

    [HttpGet("emerging")]
    public Task<IReadOnlyList<EmergingPlayerDto>> Emerging([FromQuery] int maxAge = 23, [FromQuery] string? category = null, [FromQuery] bool freeAgentsOnly = false, CancellationToken ct = default) =>
        scouting.EmergingAsync(Math.Clamp(maxAge, 16, 30), category, freeAgentsOnly, ct);
}

public record TerminateRequest(string Reason);

[Route("api/v{version:apiVersion}/finance")]
public class FinanceController(FinanceService finance) : ApiControllerBase
{
    [HttpGet("overview"), HasPermission(Perm.FinanceRead)]
    public Task<FinanceOverviewDto> Overview([FromQuery] Guid? franchiseId, CancellationToken ct) => finance.OverviewAsync(franchiseId, ct);

    [HttpGet("transactions"), HasPermission(Perm.FinanceRead)]
    public Task<PagedResult<TransactionDto>> Transactions([FromQuery] Guid? franchiseId, [FromQuery] string? category, [FromQuery] PageRequest page, CancellationToken ct) =>
        finance.TransactionsAsync(franchiseId, category, page, ct);

    [HttpPost("expenses"), HasPermission(Perm.FinanceManage)]
    public Task<TransactionDto> Expense(RecordExpenseRequest req, CancellationToken ct) => finance.RecordExpenseAsync(req, ct);

    [HttpPut("budgets"), HasPermission(Perm.FinanceManage)]
    public async Task<IActionResult> Budget(SaveBudgetRequest req, CancellationToken ct) { await finance.SaveBudgetAsync(req, ct); return NoContent(); }

    [HttpGet("contracts"), HasPermission(Perm.ContractsRead)]
    public Task<IReadOnlyList<ContractDto>> Contracts([FromQuery] Guid? franchiseId, [FromQuery] string? status, CancellationToken ct) => finance.ContractsAsync(franchiseId, status, ct);

    [HttpPost("contracts"), HasPermission(Perm.ContractsManage)]
    public async Task<object> CreateContract(SaveContractRequest req, CancellationToken ct) => new { id = await finance.SaveContractAsync(null, req, ct) };

    [HttpPut("contracts/{id:guid}"), HasPermission(Perm.ContractsManage)]
    public async Task<object> UpdateContract(Guid id, SaveContractRequest req, CancellationToken ct) => new { id = await finance.SaveContractAsync(id, req, ct) };

    [HttpPost("contracts/{id:guid}/terminate"), HasPermission(Perm.ContractsManage)]
    public async Task<IActionResult> Terminate(Guid id, TerminateRequest req, CancellationToken ct) { await finance.TerminateContractAsync(id, req.Reason, ct); return NoContent(); }

    [HttpPost("payments/{id:guid}/pay"), HasPermission(Perm.FinanceManage)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct) { await finance.MarkPaidAsync(id, ct); return NoContent(); }
}

public record PostMessageRequest(string Body);

[Route("api/v{version:apiVersion}/messages")]
public class MessagesController(MessagingService messages) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.MessagesRead)]
    public Task<IReadOnlyList<ThreadDto>> Threads(CancellationToken ct) => messages.ThreadsAsync(ct);

    [HttpGet("unread")]
    public async Task<object> Unread(CancellationToken ct) => new { count = await messages.UnreadCountAsync(ct) };

    [HttpGet("{id:guid}"), HasPermission(Perm.MessagesRead)]
    public Task<IReadOnlyList<MessageDto>> Messages(Guid id, CancellationToken ct) => messages.MessagesAsync(id, ct);

    [HttpPost, HasPermission(Perm.MessagesSend)]
    public async Task<object> Create(CreateThreadRequest req, CancellationToken ct) => new { id = await messages.CreateThreadAsync(req, ct) };

    [HttpPost("{id:guid}"), HasPermission(Perm.MessagesSend)]
    public Task<MessageDto> Post(Guid id, PostMessageRequest req, CancellationToken ct) => messages.PostAsync(id, req.Body, ct);
}

[Route("api/v{version:apiVersion}/notifications")]
public class NotificationsController(NotificationQueryService notifications) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] bool unreadOnly, [FromQuery] PageRequest page, CancellationToken ct) => notifications.ListAsync(unreadOnly, page, ct);

    [HttpGet("unread-count")]
    public async Task<object> Unread(CancellationToken ct) => new { count = await notifications.UnreadCountAsync(ct) };

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) { await notifications.MarkReadAsync(id, ct); return NoContent(); }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct) { await notifications.MarkReadAsync(null, ct); return NoContent(); }

    [HttpGet("preferences")]
    public Task<IReadOnlyList<PreferenceDto>> Preferences(CancellationToken ct) => notifications.PreferencesAsync(ct);

    [HttpPut("preferences")]
    public async Task<IActionResult> SavePreferences(IReadOnlyList<PreferenceDto> prefs, CancellationToken ct) { await notifications.SavePreferencesAsync(prefs, ct); return NoContent(); }
}

[Route("api/v{version:apiVersion}/medical")]
public class MedicalController(MedicalService medical) : ApiControllerBase
{
    [HttpGet("availability"), HasPermission(Perm.AvailabilityRead)]
    public Task<IReadOnlyList<AvailabilityRowDto>> Availability([FromQuery] Guid? franchiseId, CancellationToken ct) => medical.SquadAvailabilityAsync(franchiseId, ct);

    [HttpGet("records"), HasPermission(Perm.MedicalRead)]
    public Task<IReadOnlyList<MedicalRecordDto>> Records([FromQuery] Guid? playerId, [FromQuery] bool activeOnly, CancellationToken ct) => medical.RecordsAsync(playerId, activeOnly, ct);

    [HttpPost("records"), HasPermission(Perm.MedicalManage)]
    public async Task<object> Create(SaveMedicalRecordRequest req, CancellationToken ct) => new { id = await medical.SaveRecordAsync(null, req, ct) };

    [HttpPut("records/{id:guid}"), HasPermission(Perm.MedicalManage)]
    public async Task<object> Update(Guid id, SaveMedicalRecordRequest req, CancellationToken ct) => new { id = await medical.SaveRecordAsync(id, req, ct) };

    [HttpPost("records/{id:guid}/clear"), HasPermission(Perm.MedicalManage)]
    public async Task<IActionResult> Clear(Guid id, CancellationToken ct) { await medical.ClearAsync(id, ct); return NoContent(); }
}

[Route("api/v{version:apiVersion}/reports")]
public class ReportsController(DashboardService dashboards, ExportReportsService reports, IExportService export) : ApiControllerBase
{
    [HttpGet("dashboard"), HasPermission(Perm.ReportsRead)]
    public Task<DashboardDto> Dashboard(CancellationToken ct) => dashboards.GetAsync(ct);

    [HttpGet("types"), HasPermission(Perm.ReportsRead)]
    public IReadOnlyList<string> Types() => ExportReportsService.Types;

    [HttpGet("{type}/preview"), HasPermission(Perm.ReportsExport)]
    public Task<ReportTable> Preview(string type, [FromQuery] Guid? id, CancellationToken ct) => reports.BuildAsync(type, id, ct);

    [HttpGet("{type}/export"), HasPermission(Perm.ReportsExport)]
    public async Task<IActionResult> Export(string type, [FromQuery] string format, [FromQuery] Guid? id, CancellationToken ct)
    {
        var table = await reports.BuildAsync(type, id, ct);
        var name = $"{type}-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        return format?.ToLowerInvariant() switch
        {
            "pdf" => File(export.ToPdf(table), "application/pdf", $"{name}.pdf"),
            "xlsx" or "excel" => File(export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx"),
            "csv" => File(export.ToCsv(table), "text/csv; charset=utf-8", $"{name}.csv"),
            _ => throw new AppValidationException("format", "Use pdf, xlsx or csv."),
        };
    }
}

[Route("api/v{version:apiVersion}/audit")]
public class AuditController(AuditQueryService audit) : ApiControllerBase
{
    [HttpGet, HasPermission(Perm.AuditRead)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] string? action, [FromQuery] string? entityType, [FromQuery] string? user, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] Guid? franchiseId, [FromQuery] PageRequest page, CancellationToken ct) =>
        audit.ListAsync(action, entityType, user, from, to, franchiseId, page, ct);
}

public record UpdateRuleRequest(string Value);

[Route("api/v{version:apiVersion}/settings")]
public class SettingsController(SettingsService settings) : ApiControllerBase
{
    [HttpGet("seasons")]
    public Task<IReadOnlyList<SeasonDto>> Seasons(CancellationToken ct) => settings.SeasonsAsync(ct);

    [HttpPost("seasons"), HasPermission(Perm.SeasonsManage)]
    public async Task<object> CreateSeason(SaveSeasonRequest req, CancellationToken ct) => new { id = await settings.CreateSeasonAsync(req, ct) };

    [HttpPost("seasons/{id:int}/current"), HasPermission(Perm.SeasonsManage)]
    public async Task<IActionResult> SetCurrent(int id, CancellationToken ct) { await settings.SetCurrentAsync(id, ct); return NoContent(); }

    [HttpGet("seasons/{id:int}/rules")]
    public Task<IReadOnlyList<RuleDto>> Rules(int id, CancellationToken ct) => settings.RulesAsync(id, ct);

    [HttpPut("seasons/{id:int}/rules/{key}"), HasPermission(Perm.SettingsManage)]
    public async Task<IActionResult> UpdateRule(int id, string key, UpdateRuleRequest req, CancellationToken ct) { await settings.UpdateRuleAsync(id, key, req.Value, ct); return NoContent(); }

    [HttpGet("rating-weights")]
    public async Task<object> RatingWeights(CancellationToken ct) => System.Text.Json.JsonDocument.Parse(await settings.RatingWeightsAsync(ct)).RootElement;

    [HttpPut("rating-weights"), HasPermission(Perm.SettingsManage)]
    public async Task<IActionResult> SaveRatingWeights(RatingWeights weights, CancellationToken ct) { await settings.SaveRatingWeightsAsync(weights, ct); return NoContent(); }
}

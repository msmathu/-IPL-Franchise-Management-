using System.Text;
using ClosedXML.Excel;
using Dapper;
using Ipl.Application.Players;
using Ipl.Application.Reports;
using Ipl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Ipl.Infrastructure.Reports;

/// <summary>Aggregate queries written in SQL with Dapper (faster than EF for these read-only reports).</summary>
public class DapperReportQueries(AppDbContext db) : IReportQueries
{
    private System.Data.IDbConnection Connection => db.Database.GetDbConnection();

    public async Task<IReadOnlyList<MonthlySpendRow>> MonthlySpendAsync(Guid franchiseId, int seasonId, CancellationToken ct) =>
        (await Connection.QueryAsync<MonthlySpendRow>(new CommandDefinition("""
            SELECT YEAR(CreatedAt) AS [Year], MONTH(CreatedAt) AS [Month], Category, SUM(Amount) AS Total
            FROM ipl.FinanceTransactions
            WHERE FranchiseId = @franchiseId AND SeasonId = @seasonId AND Direction = 'Debit'
            GROUP BY YEAR(CreatedAt), MONTH(CreatedAt), Category
            ORDER BY [Year], [Month]
            """, new { franchiseId, seasonId }, cancellationToken: ct))).ToList();

    public async Task<IReadOnlyList<PerformerRow>> SquadPerformersAsync(Guid franchiseId, int seasonId, int year, CancellationToken ct) =>
        (await Connection.QueryAsync<PerformerRow>(new CommandDefinition("""
            SELECT p.Id AS PlayerId, p.FullName AS Name, r.Name AS Role,
                   SUM(s.Matches) AS Matches, SUM(s.Runs) AS Runs, SUM(s.BallsFaced) AS BallsFaced,
                   SUM(s.Wickets) AS Wickets, SUM(s.BallsBowled) AS BallsBowled, SUM(s.RunsConceded) AS RunsConceded
            FROM ipl.FranchiseSquads fs
            JOIN ipl.Players p ON p.Id = fs.PlayerId
            JOIN ipl.PlayerRoles r ON r.Id = p.PrimaryRoleId
            JOIN ipl.PlayerSeasonStatistics s ON s.PlayerId = p.Id AND s.[Year] = @year
            JOIN ipl.Competitions c ON c.Id = s.CompetitionId AND c.Format IN ('T20', 'T20I')
            WHERE fs.FranchiseId = @franchiseId AND fs.SeasonId = @seasonId AND fs.Status = 'Active'
            GROUP BY p.Id, p.FullName, r.Name
            """, new { franchiseId, seasonId, year }, cancellationToken: ct))).ToList();

    public async Task<IReadOnlyList<StandingRow>> StandingsAsync(int seasonId, CancellationToken ct)
    {
        // Standings of the latest season that has results (the current season may not have started yet).
        var rows = (await Connection.QueryAsync<StandingRow>(new CommandDefinition("""
            WITH latest AS (
                SELECT TOP 1 fs.SeasonId FROM ipl.FranchiseSeasons fs JOIN ipl.Seasons se ON se.Id = fs.SeasonId
                WHERE fs.Played > 0 ORDER BY se.[Year] DESC)
            SELECT f.Id AS FranchiseId, f.Name AS Franchise, f.ShortName, f.PrimaryColor, fs.Played, fs.Won, fs.Lost, fs.NoResult, fs.Points,
                   fs.NetRunRate, fs.FinalPosition
            FROM ipl.FranchiseSeasons fs
            JOIN ipl.Franchises f ON f.Id = fs.FranchiseId AND f.IsDeleted = 0
            WHERE fs.SeasonId = COALESCE((SELECT SeasonId FROM latest), @seasonId)
            ORDER BY fs.Points DESC, fs.NetRunRate DESC
            """, new { seasonId }, cancellationToken: ct))).ToList();
        return rows;
    }

    public async Task<IReadOnlyList<StatusCountRow>> TradeStatusCountsAsync(Guid? franchiseId, int seasonId, CancellationToken ct) =>
        (await Connection.QueryAsync<StatusCountRow>(new CommandDefinition("""
            SELECT Status, COUNT(*) AS [Count]
            FROM ipl.TradeOffers
            WHERE SeasonId = @seasonId AND Status <> 'Draft'
              AND (@franchiseId IS NULL OR InitiatorFranchiseId = @franchiseId OR CounterpartyFranchiseId = @franchiseId)
            GROUP BY Status
            """, new { franchiseId, seasonId }, cancellationToken: ct))).ToList();

    public async Task<IReadOnlyList<AuctionSpendRow>> AuctionSpendAsync(int seasonId, CancellationToken ct) =>
        (await Connection.QueryAsync<AuctionSpendRow>(new CommandDefinition("""
            SELECT f.Id AS FranchiseId, f.ShortName AS Franchise, COUNT(r.Id) AS Players,
                   COALESCE(SUM(r.Price), 0) AS Spent, COALESCE(MAX(r.Price), 0) AS MaxPrice
            FROM ipl.Franchises f
            LEFT JOIN ipl.AuctionResults r ON r.FranchiseId = f.Id AND r.Outcome = 'Sold'
                AND r.AuctionEventId IN (SELECT Id FROM ipl.AuctionEvents WHERE SeasonId = @seasonId)
            WHERE f.IsActive = 1 AND f.IsDeleted = 0
            GROUP BY f.Id, f.ShortName
            ORDER BY Spent DESC
            """, new { seasonId }, cancellationToken: ct))).ToList();
}

public class ExportService : IExportService
{
    static ExportService()
    {
        // QuestPDF Community licence: free for individuals and organisations under the revenue threshold (see docs).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] ToCsv(ReportTable t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", t.Columns.Select(Csv.Escape)));
        foreach (var row in t.Rows) sb.AppendLine(string.Join(",", row.Select(Csv.Escape)));
        // UTF-8 BOM so Excel opens ₹ and Tamil/other characters correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public byte[] ToExcel(ReportTable t)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(t.Title));
        ws.Cell(1, 1).Value = t.Title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        if (t.Subtitle is not null) ws.Cell(2, 1).Value = t.Subtitle;
        const int headerRow = 4;
        for (var c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = t.Columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b1f3a");
            cell.Style.Font.FontColor = XLColor.White;
        }
        for (var r = 0; r < t.Rows.Count; r++)
            for (var c = 0; c < t.Rows[r].Count; c++)
            {
                var cell = ws.Cell(headerRow + 1 + r, c + 1);
                switch (t.Rows[r][c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.##"; break;
                    case int i: cell.Value = i; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; break;
                    case var v: cell.Value = Csv.Escape(v).Trim('"'); break;
                }
            }
        if (t.Footnote is not null) ws.Cell(headerRow + t.Rows.Count + 2, 1).Value = t.Footnote;
        ws.Columns().AdjustToContents(1, 100);
        ws.SheetView.FreezeRows(headerRow);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ToPdf(ReportTable t) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(t.Columns.Count > 7 ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(x => x.FontSize(9));
            page.Header().Column(col =>
            {
                col.Item().Text(t.Title).FontSize(16).Bold().FontColor("#0b1f3a");
                if (t.Subtitle is not null) col.Item().Text(t.Subtitle).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingBottom(6).Text("Confidential — Franchise Management Platform").FontSize(7).FontColor(Colors.Grey.Medium);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(cols => { foreach (var _ in t.Columns) cols.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var c in t.Columns)
                        h.Cell().Background("#0b1f3a").Padding(4).Text(c).FontColor(Colors.White).Bold();
                });
                var i = 0;
                foreach (var row in t.Rows)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    foreach (var v in row)
                        table.Cell().Background(bg).Padding(3).Text(v switch
                        {
                            null => "",
                            decimal d => d.ToString("#,##0.##"),
                            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
                            _ => v.ToString() ?? "",
                        });
                }
            });
            page.Footer().Row(r =>
            {
                r.RelativeItem().Text(t.Footnote ?? "").FontSize(7).FontColor(Colors.Grey.Medium);
                r.ConstantItem(60).AlignRight().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
        });
    }).GeneratePdf();

    private static string SafeSheetName(string title)
    {
        var name = new string(title.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return name.Length > 31 ? name[..31] : name.Length == 0 ? "Report" : name;
    }
}

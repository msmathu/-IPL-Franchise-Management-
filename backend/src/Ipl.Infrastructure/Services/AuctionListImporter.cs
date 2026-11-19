using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Ipl.Application;
using Ipl.Application.Rules;
using Ipl.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Infrastructure.Services;

public sealed record AuctionListImportResult(
    int Rows, int Matched, int PlayersCreated, int LotsAdded, int LotsUpdated, int ProvisionalRemoved, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Imports the league's official auction list (CSV or Excel) into an auction: every row becomes a lot with its set,
/// lot order and base price. Existing players are matched by name (and country when names repeat); players not in the
/// database yet are created from the list (country, role, capped status, batting/bowling style, date of birth when given).
/// Columns are recognised by name, including the IPL's published format ("Set No.", "2027 Set", "First Name", "Surname",
/// "Country", "DOB", "Specialism", "Batting", "Bowling Style", "C/U/A", "Reserve Price Rs Lakh").
/// With <c>replaceProvisional</c>, pending lots from a provisional pool whose players are not on the list are removed.
/// </summary>
public class AuctionListImporter(IAppDbContext db, ICurrentUser me, IRulesService rules, IAuditService audit, IClock clock)
{
    public const string ProvisionalSetPrefix = "Provisional";

    public async Task<AuctionListImportResult> ImportAsync(Guid auctionId, Stream file, string fileName, string source, bool replaceProvisional, CancellationToken ct)
    {
        me.Require(Perm.AuctionsManage);
        if (string.IsNullOrWhiteSpace(source)) throw new AppValidationException("source", "Record where the list comes from (e.g. the official IPL auction list and its date).");
        var auction = await db.AuctionEvents.FirstOrDefaultAsync(a => a.Id == auctionId, ct) ?? throw new NotFoundException("Auction");
        if (auction.Status is AuctionStatus.Live or AuctionStatus.Completed) throw new ConflictException("The list can only be imported before the auction starts.");
        var minBase = (await rules.ForSeasonAsync(auction.SeasonId, ct)).Decimal(RuleKeys.MinBasePrice);

        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadExcel(file) : ReadCsv(file);
        if (rows.Count == 0) throw new AppValidationException("file", "The file has no rows.");

        var countries = await db.Countries.ToListAsync(ct);
        var roles = await db.PlayerRoles.ToDictionaryAsync(r => r.Code, ct);
        var players = await db.Players.Where(p => !p.IsDemo).ToListAsync(ct);
        var byName = players.GroupBy(p => Key(p.FullName)).ToDictionary(g => g.Key, g => g.ToList());
        var lots = await db.AuctionPlayers.Where(l => l.AuctionEventId == auctionId).ToListAsync(ct);
        var lotNo = lots.Count == 0 ? 0 : lots.Max(l => l.LotNumber);
        var nextCode = await NextCodeAsync(ct);
        var franchiseCodes = await db.Franchises.ToDictionaryAsync(f => f.Id, f => f.Code, ct);

        int matched = 0, created = 0, added = 0, updated = 0, skipped = 0;
        List<string> errors = [];
        var listed = new HashSet<Guid>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var line = i + 2;
            try
            {
                var name = (r.Get("player", "playername", "name", "fullname") is { Length: > 0 } full ? full : $"{r.Get("firstname")} {r.Get("surname", "lastname")}").Trim();
                name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (name.Length == 0) throw new FormatException("no player name.");
                var countryText = r.Get("country", "nationality");
                var country = FindCountry(countries, countryText);
                var basePrice = Price(r.Get("reservepricerslakh", "reservepricelakh", "basepricelakh", "basepricerslakh", "reserveprice", "baseprice"));
                if (basePrice is null) throw new FormatException($"{name}: no base price.");
                if (basePrice < minBase) throw new FormatException($"{name}: base price {Money.Format(basePrice.Value)} is below the season minimum {Money.Format(minBase)}.");
                var setNo = int.TryParse(r.Get("setno", "setnumber", "set"), out var sn) ? sn : 0;
                var setName = r.GetLike("set", exclude: ["setno", "setnumber"]) is { Length: > 0 } s && !int.TryParse(s, out _) ? s : setNo > 0 ? $"Set {setNo}" : "Auction list";

                // Find or create the player.
                Player? p = null;
                if (byName.TryGetValue(Key(name), out var same))
                    p = same.Count == 1 ? same[0] : same.FirstOrDefault(x => country is not null && x.CountryId == country.Id);
                if (p is null)
                {
                    if (country is null)
                    {
                        if (countryText.Length == 0) throw new FormatException($"{name}: country missing.");
                        country = new Country { Code = CountryCode(countries, countryText), Name = Title(countryText), IsHomeCountry = false };
                        db.Countries.Add(country);
                        countries.Add(country);
                        await db.SaveChangesAsync(ct);
                    }
                    var (roleCode, keeper) = Role(r.Get("specialism", "role", "playingrole"));
                    p = new Player
                    {
                        Code = $"P{nextCode++:00000}", FullName = name, DisplayName = name, CountryId = country.Id, IsOverseas = !country.IsHomeCountry,
                        PrimaryRoleId = roles[roleCode].Id, IsWicketKeeper = keeper, IsCapped = Capped(r.Get("cua", "capped", "cappedstatus", "status")),
                        BattingHand = Hand(r.Get("batting", "battingstyle", "battinghand")), BowlingStyle = r.Get("bowlingstyle", "bowling"),
                        DateOfBirth = Date(r.Get("dob", "dateofbirth")), Status = PlayerStatus.AvailableForAuction, CareerStatus = CareerStatus.Active,
                        DataSource = source.Trim(),
                    };
                    p.BowlingType = BowlingTypeFrom(p.BowlingStyle);
                    db.Players.Add(p);
                    players.Add(p);
                    byName[Key(name)] = [p];
                    created++;
                }
                else
                {
                    matched++;
                    if (p.CurrentFranchiseId is { } fid)
                        throw new FormatException($"{name} is in the {franchiseCodes.GetValueOrDefault(fid, "?")} squad; release or trade him first.");
                    p.Status = PlayerStatus.AvailableForAuction;
                    p.DateOfBirth ??= Date(r.Get("dob", "dateofbirth"));
                }
                if (!listed.Add(p.Id)) throw new FormatException($"{name} appears twice in the list.");

                var lot = lots.FirstOrDefault(l => l.PlayerId == p.Id);
                if (lot is null)
                {
                    lot = new AuctionPlayer { AuctionEventId = auctionId, PlayerId = p.Id, LotNumber = ++lotNo, Status = AuctionLotStatus.Pending };
                    db.AuctionPlayers.Add(lot);
                    lots.Add(lot);
                    added++;
                }
                else if (lot.Status == AuctionLotStatus.Pending) updated++;
                else { skipped++; continue; } // already sold/unsold/on the block: leave it
                lot.SetNumber = setNo;
                lot.SetName = Cut(setName, 100);
                lot.BasePrice = basePrice.Value;
            }
            catch (FormatException ex)
            {
                skipped++;
                errors.Add($"Line {line}: {ex.Message}");
            }
        }

        var removed = 0;
        if (replaceProvisional)
            foreach (var lot in lots.Where(l => l.Status == AuctionLotStatus.Pending && l.SetName.StartsWith(ProvisionalSetPrefix) && !listed.Contains(l.PlayerId)).ToList())
            {
                db.AuctionPlayers.Remove(lot);
                lots.Remove(lot);
                removed++;
            }
        // Lot numbers follow the list's set order.
        var order = 0;
        foreach (var lot in lots.Where(l => l.Status == AuctionLotStatus.Pending)
                     .OrderBy(l => l.SetName.StartsWith(ProvisionalSetPrefix)).ThenBy(l => l.SetNumber == 0 ? int.MaxValue : l.SetNumber).ThenBy(l => l.LotNumber))
            lot.LotNumber = ++order + 10000; // temporary, avoids clashes while renumbering
        foreach (var lot in lots.Where(l => l.LotNumber > 10000)) lot.LotNumber -= 10000;

        audit.Record("auctions.list_imported", "AuctionEvent", auctionId, new { source, rows = rows.Count, matched, created, added, updated, removed, skipped });
        await db.SaveChangesAsync(ct);
        return new AuctionListImportResult(rows.Count, matched, created, added, updated, removed, skipped, errors.Take(100).ToList());
    }

    // ------------------------------------------------------------------ parsing helpers

    private sealed class Row(Dictionary<string, string> values)
    {
        public string Get(params string[] keys)
        {
            foreach (var k in keys) if (values.TryGetValue(k, out var v) && v.Trim().Length > 0) return v.Trim();
            return "";
        }

        /// <summary>First column whose name contains the fragment (e.g. "2027 set"), excluding some names.</summary>
        public string GetLike(string fragment, string[] exclude) =>
            values.Where(kv => kv.Key.Contains(fragment) && !exclude.Contains(kv.Key) && !kv.Key.Contains("sr")).Select(kv => kv.Value.Trim()).FirstOrDefault(v => v.Length > 0) ?? "";
    }

    private static string Header(string h) => new(h.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static List<Row> ReadCsv(Stream s)
    {
        using var reader = new StreamReader(s);
        var text = reader.ReadToEnd();
        List<List<string>> records = [];
        List<string> rec = [];
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { rec.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                rec.Add(field.ToString()); field.Clear();
                records.Add(rec); rec = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || rec.Count > 0) { rec.Add(field.ToString()); records.Add(rec); }
        // The header is the first record that names the player (official lists may have a title row first).
        var h = records.FindIndex(r => r.Any(x => Header(x) is "firstname" or "surname" or "player" or "playername" or "name"));
        if (h < 0) return [];
        var header = records[h].Select(Header).ToList();
        return records.Skip(h + 1).Where(r => r.Any(v => v.Trim().Length > 0))
            .Select(r =>
            {
                var d = new Dictionary<string, string>();
                for (var i = 0; i < header.Count; i++) if (header[i].Length > 0) d.TryAdd(header[i], i < r.Count ? r[i] : "");
                return new Row(d);
            }).ToList();
    }

    private static List<Row> ReadExcel(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return [];
        // The header is the first row that names the player.
        var headerRow = used.Rows().FirstOrDefault(r => r.Cells().Any(c => Header(c.GetString()) is "firstname" or "surname" or "player" or "playername" or "name"));
        if (headerRow is null) return [];
        var headers = headerRow.Cells().Select(c => (c.Address.ColumnNumber, Name: Header(c.GetString()))).Where(h => h.Name.Length > 0).ToList();
        var rows = new List<Row>();
        foreach (var r in used.Rows().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            var d = new Dictionary<string, string>();
            foreach (var (col, name) in headers)
            {
                var cell = ws.Cell(r.RowNumber(), col);
                d.TryAdd(name, cell.DataType == XLDataType.DateTime ? cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : cell.GetFormattedString());
            }
            if (d.Values.Any(v => v.Trim().Length > 0)) rows.Add(new Row(d));
        }
        return rows;
    }

    private static string Key(string name)
    {
        var d = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        var t = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("mohd ", "mohammed ").Replace("mohammad ", "mohammed ");
        return t;
    }

    private static decimal? Price(string text)
    {
        var t = text.Replace(",", "").Replace("₹", "").Trim();
        if (t.Length == 0) return null;
        var lower = t.ToLowerInvariant();
        decimal mult = lower.Contains("cr") ? Money.Crore : lower.Contains("l") ? Money.Lakh : 0;
        var num = new string(t.Where(c => char.IsDigit(c) || c == '.').ToArray());
        if (!decimal.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return null;
        if (mult == 0) mult = v >= 10_000 ? 1 : Money.Lakh; // plain numbers: rupees if large, otherwise lakh (IPL lists use lakh)
        return v * mult;
    }

    private static Country? FindCountry(List<Country> countries, string text)
    {
        if (text.Length == 0) return null;
        var t = text.Trim();
        return countries.FirstOrDefault(c => c.Name.Equals(t, StringComparison.OrdinalIgnoreCase) || c.Code.Equals(t, StringComparison.OrdinalIgnoreCase))
            ?? (t.Equals("South Africa", StringComparison.OrdinalIgnoreCase) || t.Equals("SA", StringComparison.OrdinalIgnoreCase) ? countries.FirstOrDefault(c => c.Code == "RSA") : null)
            ?? (t.Equals("Sri Lanka", StringComparison.OrdinalIgnoreCase) || t.Equals("SL", StringComparison.OrdinalIgnoreCase) ? countries.FirstOrDefault(c => c.Code == "SRI") : null)
            ?? (t.Equals("New Zealand", StringComparison.OrdinalIgnoreCase) || t.Equals("NZ", StringComparison.OrdinalIgnoreCase) ? countries.FirstOrDefault(c => c.Code == "NZL") : null)
            ?? (t.Equals("West Indies", StringComparison.OrdinalIgnoreCase) || t.Equals("WI", StringComparison.OrdinalIgnoreCase) ? countries.FirstOrDefault(c => c.Code == "WIN") : null);
    }

    private static string CountryCode(List<Country> countries, string name)
    {
        var letters = new string(name.ToUpperInvariant().Where(char.IsLetter).ToArray());
        var code = letters.Length >= 3 ? letters[..3] : letters.PadRight(3, 'X');
        for (var n = 0; countries.Any(c => c.Code == code); n++) code = letters[..2] + (char)('A' + n);
        return code;
    }

    private static (string Code, bool Keeper) Role(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("keep") || t.Contains("wk")) return ("wk_batter", true);
        if (t.Contains("all")) return ("allrounder", false);
        if (t.Contains("bowl")) return ("bowler", false);
        return ("batter", false);
    }

    private static bool Capped(string text) => text.Trim().ToLowerInvariant() is "c" or "capped" or "yes" or "true" or "1";

    private static BattingHand Hand(string text)
    {
        var t = text.ToLowerInvariant();
        return t.StartsWith("l") ? BattingHand.LeftHand : t.StartsWith("r") ? BattingHand.RightHand : BattingHand.Unknown;
    }

    private static BowlingType BowlingTypeFrom(string style)
    {
        var s = style.ToLowerInvariant();
        if (s.Length == 0) return BowlingType.Unknown;
        if (s.Contains("leg") || s.Contains("wrist") || s.Contains("googly")) return s.Contains("left") ? BowlingType.LeftArmWristSpin : BowlingType.LegSpin;
        if (s.Contains("off")) return BowlingType.OffSpin;
        if (s.Contains("orthodox") || (s.Contains("left") && s.Contains("spin")) || s.Contains("sla")) return BowlingType.LeftArmOrthodox;
        if (s.Contains("fast-medium") || s.Contains("fast medium") || s.Contains("medium-fast") || s.Contains("medium fast")) return BowlingType.MediumFast;
        if (s.Contains("fast")) return BowlingType.Fast;
        if (s.Contains("medium")) return BowlingType.Medium;
        return BowlingType.Unknown;
    }

    private static DateOnly? Date(string text)
    {
        string[] formats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yy", "d-MMM-yy", "dd-MMM-yyyy"];
        return DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string Title(string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<int> NextCodeAsync(CancellationToken ct)
    {
        var codes = await db.Players.IgnoreQueryFilters().Select(p => p.Code).ToListAsync(ct);
        return codes.Select(c => int.TryParse(c.TrimStart('P', 'D'), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
    }
}

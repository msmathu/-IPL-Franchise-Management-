using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ipl.Application;
using Ipl.Domain;
using Ipl.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ipl.Infrastructure.Services;

public class MediaOptions
{
    /// <summary>Folder for downloaded images (served at /media). Relative paths are under the API's content root.</summary>
    public string Folder { get; set; } = "App_Data/media";
}

public sealed record PhotoRunResult(
    DateTime StartedAt, DateTime FinishedAt, bool Succeeded, string? Error,
    int PlayersChecked, int PhotosAdded, int PhotosUnchanged, int WithoutFreePhoto, IReadOnlyList<string> Skipped);

public sealed record PhotoStatusDto(bool Running, string? Stage, PhotoRunResult? LastRun, int PlayersWithPhoto, int RealPlayers);

/// <summary>
/// Downloads freely licensed player photos from Wikimedia Commons. Players are identified exactly:
/// Cricsheet id → ESPNcricinfo id (Cricsheet register) → Wikidata item (property P2697) → image (P18), or the
/// free lead image of the player's English Wikipedia article. Every photo keeps its author, licence and source page.
/// Photos set by hand (any URL not under /media/players/) are never replaced.
/// </summary>
public class PlayerPhotoImporter(AppDbContext db, IAuditService audit, IClock clock, MediaFolder media, ILogger<PlayerPhotoImporter> log)
{
    public const string LastRunKey = "photos.lastRun";
    public const string LocalPrefix = "/media/players/";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("FranchiseHQ-PhotoImporter/1.0 (self-hosted cricket management software)");
        return c;
    }

    public async Task<PhotoRunResult> RunAsync(Action<string> stage, CancellationToken ct)
    {
        var started = clock.UtcNow;
        stage("Reading the Cricsheet register");
        var cricinfoByCricsheet = await CricinfoIdsAsync(ct);

        var players = await db.Players.Where(p => !p.IsDemo && p.CricsheetId != null && (p.PhotoUrl == null || p.PhotoUrl.StartsWith(LocalPrefix)))
            .ToListAsync(ct);
        var byCricinfo = new Dictionary<string, Player>();
        foreach (var p in players)
            if (cricinfoByCricsheet.TryGetValue(p.CricsheetId!, out var cid)) byCricinfo.TryAdd(cid, p);

        stage("Looking up Wikidata");
        var files = new Dictionary<Player, string>();
        var articles = new Dictionary<string, Player>();
        foreach (var chunk in byCricinfo.Keys.Chunk(150))
        {
            var query = "SELECT ?cid ?image ?article WHERE { VALUES ?cid { " + string.Join(' ', chunk.Select(c => $"\"{c}\"")) + " } " +
                "?item wdt:P2697 ?cid. OPTIONAL { ?item wdt:P18 ?image } " +
                "OPTIONAL { ?article schema:about ?item; schema:isPartOf <https://en.wikipedia.org/> } }";
            using var doc = await GetJsonAsync("https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(query), ct);
            foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
            {
                var player = byCricinfo[b.GetProperty("cid").GetProperty("value").GetString()!];
                if (b.TryGetProperty("image", out var img))
                    files[player] = Uri.UnescapeDataString(img.GetProperty("value").GetString()!.Split('/')[^1]);
                else if (b.TryGetProperty("article", out var art) && !files.ContainsKey(player))
                    articles[Uri.UnescapeDataString(art.GetProperty("value").GetString()!.Split("/wiki/")[^1]).Replace('_', ' ')] = player;
            }
        }

        stage("Checking Wikipedia lead images");
        foreach (var chunk in articles.Keys.Where(t => !files.ContainsKey(articles[t])).Chunk(50))
        {
            using var doc = await GetJsonAsync("https://en.wikipedia.org/w/api.php?action=query&format=json&prop=pageimages&piprop=name&pilicense=free&redirects=1&titles="
                + Uri.EscapeDataString(string.Join('|', chunk)), ct);
            var q = doc.RootElement.GetProperty("query");
            var renamed = new Dictionary<string, string>();
            foreach (var key in new[] { "normalized", "redirects" })
                if (q.TryGetProperty(key, out var list))
                    foreach (var n in list.EnumerateArray()) renamed[n.GetProperty("to").GetString()!] = n.GetProperty("from").GetString()!;
            foreach (var page in q.GetProperty("pages").EnumerateObject())
            {
                if (!page.Value.TryGetProperty("pageimage", out var pi)) continue;
                var title = page.Value.GetProperty("title").GetString()!;
                while (renamed.TryGetValue(title, out var from)) title = from;
                if (articles.TryGetValue(title, out var player)) files.TryAdd(player, pi.GetString()!.Replace('_', ' '));
            }
        }

        stage("Downloading photos");
        int added = 0, unchanged = 0;
        List<string> skipped = [];
        foreach (var chunk in files.Chunk(40))
        {
            var titles = chunk.Select(f => "File:" + f.Value).ToList();
            using var doc = await GetJsonAsync("https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=320&titles="
                + Uri.EscapeDataString(string.Join('|', titles)), ct);
            var q = doc.RootElement.GetProperty("query");
            var normalized = new Dictionary<string, string>();
            if (q.TryGetProperty("normalized", out var norm))
                foreach (var n in norm.EnumerateArray()) normalized[n.GetProperty("to").GetString()!] = n.GetProperty("from").GetString()!;
            var info = new Dictionary<string, JsonElement>();
            foreach (var page in q.GetProperty("pages").EnumerateObject())
                if (page.Value.TryGetProperty("imageinfo", out var ii))
                {
                    var title = page.Value.GetProperty("title").GetString()!;
                    info[normalized.GetValueOrDefault(title, title)] = ii[0].Clone();
                }

            foreach (var (player, file) in chunk)
            {
                if (!info.TryGetValue("File:" + file, out var ii) && !info.TryGetValue("File:" + file.Replace('_', ' '), out ii))
                {
                    skipped.Add($"{player.FullName}: image not found on Commons");
                    continue;
                }
                var meta = ii.GetProperty("extmetadata");
                var licence = Meta(meta, "LicenseShortName");
                if (licence is null)
                {
                    skipped.Add($"{player.FullName}: no licence information");
                    continue;
                }
                var author = Meta(meta, "Artist") ?? "Unknown author";
                var source = ii.GetProperty("descriptionurl").GetString();
                var thumb = (ii.TryGetProperty("thumburl", out var t) ? t.GetString() : null) ?? ii.GetProperty("url").GetString()!;
                var credit = Truncate($"Photo: {author} · {licence} · Wikimedia Commons", 400);
                if (player.PhotoUrl is not null && player.PhotoSourceUrl == source && File.Exists(media.PathFor(player.PhotoUrl)))
                {
                    player.PhotoCredit = credit;
                    unchanged++;
                    continue;
                }
                try
                {
                    using var res = await Http.GetAsync(thumb, ct);
                    res.EnsureSuccessStatusCode();
                    var type = res.Content.Headers.ContentType?.MediaType ?? "";
                    var ext = type switch { "image/jpeg" => "jpg", "image/png" => "png", "image/webp" => "webp", "image/gif" => "gif", _ => null };
                    if (ext is null) { skipped.Add($"{player.FullName}: unsupported image type {type}"); continue; }
                    var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                    if (bytes.Length > 3_000_000) { skipped.Add($"{player.FullName}: image too large"); continue; }
                    var name = $"{player.Code}.{ext}";
                    await File.WriteAllBytesAsync(Path.Combine(media.PlayersFolder, name), bytes, ct);
                    player.PhotoUrl = $"{LocalPrefix}{name}?v={clock.UtcNow:yyyyMMddHHmmss}";
                    player.PhotoCredit = credit;
                    player.PhotoSourceUrl = Truncate(source ?? "", 400);
                    added++;
                }
                catch (HttpRequestException ex)
                {
                    skipped.Add($"{player.FullName}: download failed ({ex.StatusCode})");
                }
                await Task.Delay(150, ct); // be gentle with Wikimedia's servers
            }
        }
        await db.SaveChangesAsync(ct);

        var result = new PhotoRunResult(started, clock.UtcNow, true, null, players.Count, added, unchanged,
            players.Count - files.Count, skipped);
        audit.Record("players.photos-imported", "Player", null, new { added, unchanged });
        await SaveLastRunAsync(db, result, clock, ct);
        log.LogInformation("Photo import: {Added} added, {Unchanged} unchanged, {Skipped} skipped", added, unchanged, skipped.Count);
        return result;
    }

    public static async Task SaveLastRunAsync(AppDbContext db, PhotoRunResult result, IClock clock, CancellationToken ct)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == LastRunKey, ct);
        if (row is null) db.SystemSettings.Add(row = new SystemSetting { Key = LastRunKey, Description = "Result of the last player photo import." });
        row.Value = JsonSerializer.Serialize(result);
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Cricsheet id → ESPNcricinfo id, from the Cricsheet people register.</summary>
    private static async Task<Dictionary<string, string>> CricinfoIdsAsync(CancellationToken ct)
    {
        var csv = await Http.GetStringAsync("https://cricsheet.org/register/people.csv", ct);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = SplitCsv(lines[0]);
        var idCol = Array.IndexOf(header, "identifier");
        var ciCol = Array.IndexOf(header, "key_cricinfo");
        var map = new Dictionary<string, string>();
        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsv(line.TrimEnd('\r'));
            if (cols.Length > ciCol && cols[ciCol].Length > 0) map[cols[idCol]] = cols[ciCol];
        }
        return map;
    }

    private static string[] SplitCsv(string line)
    {
        List<string> cols = [];
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { cols.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cols.Add(sb.ToString());
        return [.. cols];
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var res = await Http.GetAsync(url, ct);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }
            res.EnsureSuccessStatusCode();
            return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
    }

    private static string? Meta(JsonElement meta, string key)
    {
        if (!meta.TryGetProperty(key, out var v) || !v.TryGetProperty("value", out var s)) return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(s.GetString() ?? "", "<[^>]+>", "")).Trim();
        text = Regex.Replace(text, @"\s+", " ");
        return text.Length == 0 ? null : Truncate(text, 150);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Resolved media folder (created on start).</summary>
public class MediaFolder
{
    public MediaFolder(MediaOptions options, IHostEnvironment env)
    {
        Root = Path.IsPathRooted(options.Folder) ? options.Folder : Path.Combine(env.ContentRootPath, options.Folder);
        PlayersFolder = Path.Combine(Root, "players");
        Directory.CreateDirectory(PlayersFolder);
    }

    public string Root { get; }
    public string PlayersFolder { get; }

    /// <summary>Local file for a "/media/..." URL (query string ignored).</summary>
    public string PathFor(string url) => Path.Combine(Root, url.Split('?')[0]["/media/".Length..].Replace('/', Path.DirectorySeparatorChar));
}

public class PhotoImportRunner(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, ILogger<PhotoImportRunner> log)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile string? stage;

    public bool TryStart()
    {
        if (!gate.Wait(0)) return false;
        stage = "Starting";
        _ = Task.Run(async () =>
        {
            var started = DateTime.UtcNow;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PlayerPhotoImporter>().RunAsync(s => stage = s, lifetime.ApplicationStopping);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Photo import failed");
                try
                {
                    using var scope = scopes.CreateScope();
                    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                    await PlayerPhotoImporter.SaveLastRunAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                        new PhotoRunResult(started, clock.UtcNow, false, ex.Message, 0, 0, 0, 0, []), clock, CancellationToken.None);
                }
                catch (Exception inner) { log.LogError(inner, "Could not record the failed photo import"); }
            }
            finally
            {
                stage = null;
                gate.Release();
            }
        });
        return true;
    }

    public async Task<PhotoStatusDto> StatusAsync(AppDbContext db, CancellationToken ct)
    {
        var json = await db.SystemSettings.AsNoTracking().Where(s => s.Key == PlayerPhotoImporter.LastRunKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var withPhoto = await db.Players.CountAsync(p => !p.IsDemo && p.PhotoUrl != null, ct);
        var real = await db.Players.CountAsync(p => !p.IsDemo, ct);
        return new PhotoStatusDto(stage is not null, stage, json is null ? null : JsonSerializer.Deserialize<PhotoRunResult>(json), withPhoto, real);
    }
}

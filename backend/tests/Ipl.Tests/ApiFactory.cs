using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ipl.Tests;

/// <summary>
/// Runs the real API in memory against a throw-away SQL Server database (created per test run and dropped
/// afterwards). Set IPL_TEST_SQL to point at another server; the default is the local instance.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Demo@2026pass";
    private readonly string database = $"IplFranchise_Test_{Guid.NewGuid():N}";
    private string Server => Environment.GetEnvironmentVariable("IPL_TEST_SQL") ?? @".\MSSQLSERVERSE";
    public string ConnectionString => $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-0123456789");
        builder.UseSetting("DemoData:Enabled", "true");
        builder.UseSetting("DemoData:HistoricSeasons", "1");
        builder.UseSetting("RateLimits:AuthPerMinute", "10000");
        builder.UseSetting("RateLimits:ApiBurst", "100000");
        builder.UseSetting("RateLimits:ApiPerSecond", "100000");
        builder.ConfigureServices(services =>
        {
            // Background jobs would race with the tests; the tests call the jobs directly where needed.
            foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList()) services.Remove(d);
        });
    }

    public async Task InitializeAsync()
    {
        // Creating the first client starts the host, which migrates and seeds the test database.
        using var client = CreateClient();
        await Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection($"Server={Server};Database=master;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A client signed in as a demo user (the refresh cookie is kept for refresh tests).</summary>
    public async Task<ApiClient> LoginAsync(string email)
    {
        var http = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var client = new ApiClient(http);
        var res = await client.PostAsync("auth/login", new { email, password = Password });
        Assert.True(res.Status == HttpStatusCode.OK, $"login {email}: {res.Raw}");
        client.SetToken(res.Data.GetProperty("accessToken").GetString()!);
        client.RefreshCookie = res.SetCookie;
        return client;
    }
}

public record ApiResult(HttpStatusCode Status, JsonElement Data, JsonElement Error, string Raw, string? SetCookie)
{
    public string ErrorCode => Error.ValueKind == JsonValueKind.Object ? Error.GetProperty("code").GetString() ?? "" : "";
}

public class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public HttpClient Http => http;
    public string? RefreshCookie { get; set; }
    public string? Token { get; private set; }

    public void SetToken(string token)
    {
        Token = token;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public Task<ApiResult> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<ApiResult> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body ?? new { });
    public Task<ApiResult> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);
    public Task<ApiResult> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<ApiResult> SendAsync(HttpMethod method, string path, object? body, string? cookie = null)
    {
        using var req = new HttpRequestMessage(method, $"/api/v1/{path}");
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        using var res = await http.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var setCookie = res.Headers.TryGetValues("Set-Cookie", out var values) ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("ipl_refresh=")) : null;
        JsonElement data = default, error = default;
        if (raw.Length > 0 && raw.TrimStart().StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var d)) data = d.Clone();
            if (doc.RootElement.TryGetProperty("error", out var e)) error = e.Clone();
        }
        return new ApiResult(res.StatusCode, data, error, raw.Length > 600 ? raw[..600] : raw, setCookie);
    }

    public async Task<JsonElement> OkAsync(Task<ApiResult> call)
    {
        var r = await call;
        Assert.True((int)r.Status is >= 200 and < 300, $"Expected success but got {(int)r.Status}: {r.Raw}");
        return r.Data;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;

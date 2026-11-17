using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Ipl.Api.Infrastructure;
using Ipl.Application;
using Ipl.Application.Identity;
using Ipl.Infrastructure;
using Ipl.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters. Set it with user-secrets or the Jwt__SigningKey environment variable.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IRealtimePublisher, SignalRPublisher>();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("ipl-franchise");

// ---------- Authentication: short-lived JWT access tokens + rotating refresh cookie ----------
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = JwtRegisteredClaimNames.Name,
    };
    o.Events = new JwtBearerEvents
    {
        // SignalR sends the token in the query string.
        OnMessageReceived = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query["access_token"] is { Count: > 0 } t) ctx.Token = t;
            return Task.CompletedTask;
        },
        // Deactivated users and changed roles/passwords invalidate existing tokens straight away.
        OnTokenValidated = async ctx =>
        {
            var sub = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var stamp = ctx.Principal?.FindFirst(Claims.SecurityStamp)?.Value;
            if (!Guid.TryParse(sub, out var userId) || stamp is null) { ctx.Fail("Invalid token."); return; }
            var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var current = await cache.GetOrCreateAsync($"stamp:{userId}", async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                return await db.Users.AsNoTracking().Where(u => u.Id == userId && u.IsActive).Select(u => u.SecurityStamp).FirstOrDefaultAsync();
            });
            if (current != stamp) ctx.Fail("Session is no longer valid.");
        },
    };
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---------- MVC, versioning, validation envelope ----------
builder.Services.AddControllers(o => o.Filters.Add<ApiEnvelopeFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var details = ctx.ModelState.Where(m => m.Value?.Errors.Count > 0)
            .ToDictionary(m => m.Key, m => m.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());
        var first = details.FirstOrDefault();
        return new BadRequestObjectResult(new ApiResponse<object>(false, null,
            new ApiError("validation", first.Value?.FirstOrDefault() ?? "The request is invalid.", first.Key, ctx.HttpContext.TraceIdentifier, details)));
    });
builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddMvc().AddApiExplorer(o => { o.GroupNameFormat = "'v'V"; o.SubstituteApiVersionInUrl = true; });
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Franchise Management Platform API", Version = "v1",
        Description = "All responses use { success, data, error }. Authenticate with POST /api/v1/auth/login and send 'Authorization: Bearer <token>'.",
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    o.CustomSchemaIds(t => t.FullName!.Replace("+", "."));
});

// ---------- Rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) =>
        await ctx.HttpContext.Response.WriteAsJsonAsync(new ApiResponse<object>(false, null, new ApiError("rate_limited", "Too many requests. Please wait a moment.")), ct);
    // Login, password reset and invitations: per IP.
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.ClientIp() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    // Session refresh: every page load refreshes once, so allow more than for logins.
    o.AddPolicy("refresh", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.ClientIp() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:RefreshPerMinute", 60), Window = TimeSpan.FromMinutes(1) }));
    // Bids: per user, generous enough for a live auction but stops scripted flooding.
    o.AddPolicy("bids", ctx => RateLimitPartition.GetSlidingWindowLimiter(ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? ctx.ClientIp() ?? "unknown",
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromSeconds(10), SegmentsPerWindow = 5 }));
    // Everything else: per user (or IP when anonymous).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetTokenBucketLimiter(ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? ctx.ClientIp() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = builder.Configuration.GetValue("RateLimits:ApiBurst", 300), TokensPerPeriod = builder.Configuration.GetValue("RateLimits:ApiPerSecond", 20),
                ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 0,
            }));
});

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

if (builder.Configuration.GetValue("Database:InitializeOnStartup", true))
    await app.Services.InitializeDatabaseAsync(builder.Configuration.GetValue("Database:Migrate", true));

if (builder.Configuration.GetValue("ReverseProxy:Enabled", false)) app.UseForwardedHeaders();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information);
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!ctx.Request.Path.StartsWithSegments("/swagger"))
        h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseHsts();
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"));
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("web");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<LiveHub>("/hubs/live");
app.MapHealthChecks("/health").AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public partial class Program;

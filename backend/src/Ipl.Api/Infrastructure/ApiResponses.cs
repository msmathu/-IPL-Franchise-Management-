using System.Diagnostics;
using Ipl.Application;
using Ipl.Application.Identity;
using Ipl.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ipl.Api.Infrastructure;

/// <summary>Every JSON response uses the same envelope: { success, data } or { success: false, error }.</summary>
public interface IApiResponse;

public record ApiResponse<T>(bool Success, T? Data, ApiError? Error) : IApiResponse;

public record ApiError(string Code, string Message, string? Field = null, string? TraceId = null, IDictionary<string, string[]>? Details = null);

/// <summary>Wraps successful action results in the envelope (files and empty results are left alone).</summary>
public class ApiEnvelopeFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult result && (result.StatusCode ?? 200) is >= 200 and < 300 && result.Value is not IApiResponse)
        {
            result.Value = new ApiResponse<object>(true, result.Value, null);
            result.DeclaredType = typeof(ApiResponse<object>);
        }
        return next();
    }
}

/// <summary>Maps exceptions to HTTP status codes and the error envelope; never leaks internals.</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;
        var (status, error) = ex switch
        {
            AppValidationException v => (400, new ApiError("validation", v.Message, v.Field, traceId)),
            AuthFailedException a => (401, new ApiError("unauthorized", a.Message, null, traceId)),
            ForbiddenException f => (403, new ApiError("forbidden", f.Message, null, traceId)),
            NotFoundException n => (404, new ApiError("not_found", n.Message, null, traceId)),
            ConflictException c => (409, new ApiError("conflict", c.Message, null, traceId)),
            DbUpdateConcurrencyException => (409, new ApiError("concurrency", "Someone else changed this at the same time. Reload and try again.", null, traceId)),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, new ApiError("duplicate", "This record already exists (duplicate request).", null, traceId)),
            DomainException d => (422, new ApiError(d.Code, d.Message, null, traceId)),
            BadHttpRequestException b => (b.StatusCode, new ApiError("bad_request", "The request could not be read.", null, traceId)),
            SqlException when ctx.RequestAborted.IsCancellationRequested => (499, new ApiError("cancelled", "Request cancelled.", null, traceId)),
            OperationCanceledException when ctx.RequestAborted.IsCancellationRequested => (499, new ApiError("cancelled", "Request cancelled.", null, traceId)),
            _ => (500, new ApiError("server_error", env.IsDevelopment() ? ex.Message : "Something went wrong. Please try again; if it keeps happening, quote the trace id.", null, traceId)),
        };
        if (status >= 500) log.LogError(ex, "Unhandled error {TraceId}", traceId);
        else if (status != 499) log.LogInformation("Request failed {Status} {Code}: {Message}", status, error.Code, error.Message);
        if (ex is DbUpdateConcurrencyException dce)
            log.LogWarning("Concurrency conflict on {Entities} {TraceId}", string.Join(", ", dce.Entries.Select(e => $"{e.Metadata.ClrType.Name}:{e.State}")), traceId);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ApiResponse<object>(false, null, error), ct);
        return true;
    }
}

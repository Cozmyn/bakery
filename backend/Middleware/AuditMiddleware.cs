using System.Diagnostics;
using System.Text.Json;
using Bakery.Api.Models;
using Bakery.Api.Services;

namespace Bakery.Api.Middleware;

/// <summary>
/// Request-level audit logging.
/// - Logs all mutating requests (non-GET/HEAD).
/// - Logs prompt resolutions, admin edits, batch edits, etc.
/// - Does NOT store sensitive secrets (passwords).
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, AuditService audit)
    {
        var sw = Stopwatch.StartNew();
        await _next(ctx);
        sw.Stop();

        var req = ctx.Request;
        var method = req.Method.ToUpperInvariant();
        var shouldLog = method != "GET" && method != "HEAD" && method != "OPTIONS";

        // Always log prompt resolution and auth login attempts (compliance).
        var path = req.Path.Value ?? "/";
        var isLogin = path.StartsWith("/auth/login", StringComparison.OrdinalIgnoreCase);
        var isPromptResolve = path.Contains("/prompts/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/resolve", StringComparison.OrdinalIgnoreCase);

        if (!shouldLog && !isLogin && !isPromptResolve) return;

        var (email, role) = AuditService.ReadUser(ctx.User);
        var (entityType, entityId) = AuditService.GuessEntity(path);

        // Body preview: never store passwords
        string? bodyPreview = null;
        if (isLogin)
        {
            // Only keep email if present, discard password
            bodyPreview = "{\"note\":\"login attempt\"}";
        }
        else
        {
            bodyPreview = await AuditService.ReadBodyPreviewAsync(req, maxChars: 20_000, ctx.RequestAborted);
        }

        var detail = AuditService.SafeJson(new
        {
            query = req.QueryString.HasValue ? req.QueryString.Value : null,
            body = bodyPreview,
            durationMs = (int)sw.ElapsedMilliseconds
        });

        var entry = new AuditLog
        {
            TsUtc = DateTime.UtcNow,
            Method = method,
            Path = path,
            Action = $"{method} {path}",
            UserEmail = email,
            UserRole = role,
            EntityType = entityType,
            EntityId = entityId,
            IpAddress = ctx.Connection.RemoteIpAddress?.ToString(),
            StatusCode = ctx.Response.StatusCode,
            DetailJson = detail
        };

        await audit.WriteAsync(entry, ctx.RequestAborted);
    }
}

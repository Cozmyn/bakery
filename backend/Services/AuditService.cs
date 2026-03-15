using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bakery.Api.Data;
using Bakery.Api.Models;

namespace Bakery.Api.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _log;

    public AuditService(AppDbContext db, ILogger<AuditService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task WriteAsync(AuditLog entry, CancellationToken ct = default)
    {
        try
        {
            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never break production flow due to audit write.
            _log.LogWarning(ex, "Audit write failed");
        }
    }

    public static (string? email, string? role) ReadUser(ClaimsPrincipal user)
    {
        var email = user.Identity?.Name;
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        return (email, role);
    }

    public static (string? entityType, string? entityId) GuessEntity(string path)
    {
        var segs = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return (null, null);

        var entityType = segs[0];
        string? entityId = null;

        if (segs.Length >= 2)
        {
            var s = segs[1];
            if (Guid.TryParse(s, out _)) entityId = s;
            else if (s.Length <= 80) entityId = s; // e.g., codes
        }
        return (entityType, entityId);
    }

    public static string SafeJson(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    public static async Task<string?> ReadBodyPreviewAsync(HttpRequest req, int maxChars, CancellationToken ct)
    {
        if (req.ContentLength is null || req.ContentLength == 0) return null;
        if (req.ContentLength > 64_000) return "{\"note\":\"body too large\"}";

        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        req.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body)) return null;
        if (body.Length > maxChars) body = body[..maxChars] + "…";
        return body;
    }
}

using Bakery.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("audit")]
[Authorize(Policy = "AdminOnly")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int days = 7,
        [FromQuery] int limit = 200,
        [FromQuery] int offset = 0,
        [FromQuery] string? userEmail = null,
        [FromQuery] string? entityType = null)
    {
        days = Math.Clamp(days, 1, 3650);
        limit = Math.Clamp(limit, 10, 1000);
        offset = Math.Clamp(offset, 0, 1_000_000);

        var since = DateTime.UtcNow.AddDays(-days);

        var q = _db.AuditLogs.AsNoTracking()
            .Where(x => x.TsUtc >= since);

        if (!string.IsNullOrWhiteSpace(userEmail))
        {
            var ue = userEmail.Trim().ToLowerInvariant();
            q = q.Where(x => x.UserEmail != null && x.UserEmail.ToLower().Contains(ue));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var et = entityType.Trim().ToLowerInvariant();
            q = q.Where(x => x.EntityType != null && x.EntityType.ToLower().Contains(et));
        }

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(x => x.TsUtc)
            .Skip(offset)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.TsUtc,
                x.Method,
                x.Path,
                x.Action,
                x.UserEmail,
                x.UserRole,
                x.EntityType,
                x.EntityId,
                x.IpAddress,
                x.StatusCode,
                x.DetailJson
            })
            .ToListAsync();

        return Ok(new { total, items });
    }
}

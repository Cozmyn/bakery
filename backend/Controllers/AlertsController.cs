using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("alerts")]
[Authorize(Policy = "OperatorOrAdmin")]
public class AlertsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AlertsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("active")]
    public async Task<IActionResult> Active([FromQuery] Guid? runId = null)
    {
        var now = DateTime.UtcNow;
        var q = _db.Alerts.AsNoTracking()
            .Where(a => a.Status != AlertStatus.Closed);

        if (runId != null) q = q.Where(a => a.RunId == runId);

        // Hide snoozed alerts until snooze expires
        q = q.Where(a => a.Status != AlertStatus.Snoozed || (a.SnoozedUntilUtc != null && a.SnoozedUntilUtc <= now));

        var items = await q
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.TriggeredAtUtc)
            .Take(50)
            .Select(a => new
            {
                a.Id,
                a.RunId,
                a.Type,
                a.Title,
                a.Message,
                severity = a.Severity.ToString(),
                status = a.Status.ToString(),
                a.TriggeredAtUtc,
                a.SnoozedUntilUtc,
                a.AcknowledgedByEmail,
                a.AcknowledgedAtUtc,
                a.MetadataJson
            })
            .ToListAsync();

        return Ok(items);
    }

    public record AckReq(string? Note);

    [HttpPost("{id:guid}/ack")]
    public async Task<IActionResult> Ack(Guid id, [FromBody] AckReq req)
    {
        var a = await _db.Alerts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var email = User?.Identity?.Name ?? User?.Claims?.FirstOrDefault(c => c.Type.EndsWith("emailaddress", StringComparison.OrdinalIgnoreCase))?.Value;
        a.Status = AlertStatus.Acknowledged;
        a.AcknowledgedByEmail = email;
        a.AcknowledgedAtUtc = DateTime.UtcNow;
        a.UpdatedAtUtc = a.AcknowledgedAtUtc;
        a.UpdatedBy = email;
        if (!string.IsNullOrWhiteSpace(req?.Note))
        {
            a.MetadataJson = a.MetadataJson; // keep metadata
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public record SnoozeReq(int Minutes);

    [HttpPost("{id:guid}/snooze")]
    public async Task<IActionResult> Snooze(Guid id, [FromBody] SnoozeReq req)
    {
        var a = await _db.Alerts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        var minutes = Math.Clamp(req.Minutes, 1, 240);
        a.Status = AlertStatus.Snoozed;
        a.SnoozedUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
        a.UpdatedAtUtc = DateTime.UtcNow;
        a.UpdatedBy = User?.Identity?.Name;

        await _db.SaveChangesAsync();
        return Ok(new { ok = true, snoozedUntilUtc = a.SnoozedUntilUtc });
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id)
    {
        var a = await _db.Alerts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();

        a.Status = AlertStatus.Closed;
        a.ClosedAtUtc = DateTime.UtcNow;
        a.UpdatedAtUtc = a.ClosedAtUtc;
        a.UpdatedBy = User?.Identity?.Name;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("history")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> History([FromQuery] int days = 7)
    {
        days = Math.Clamp(days, 1, 365);
        var to = DateTime.UtcNow;
        var from = to.AddDays(-days);

        var items = await _db.Alerts.AsNoTracking()
            .Where(a => a.TriggeredAtUtc >= from && a.TriggeredAtUtc <= to)
            .OrderByDescending(a => a.TriggeredAtUtc)
            .Take(200)
            .Select(a => new { a.Id, a.RunId, a.Type, a.Title, a.Message, severity = a.Severity.ToString(), status = a.Status.ToString(), a.TriggeredAtUtc, a.AcknowledgedAtUtc, a.ClosedAtUtc })
            .ToListAsync();

        return Ok(new { fromUtc = from, toUtc = to, items });
    }
}

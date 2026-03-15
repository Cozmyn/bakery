using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("defect-types")]
[Authorize(Policy = "OperatorOrAdmin")]
public class DefectTypesController : ControllerBase
{
    private readonly AppDbContext _db;
    public DefectTypesController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        var q = _db.DefectTypes.AsNoTracking();
        if (!includeInactive) q = q.Where(x => x.IsActive);

        var list = await q.OrderBy(x => x.SortOrder)
            .Select(x => new { code = x.Code, label = x.Label, category = x.Category, severity = x.SeverityDefault, isActive = x.IsActive })
            .ToListAsync();

        return Ok(list);
    }

    public record UpsertReq(string Code, string Label, string Category, int SeverityDefault, int SortOrder, bool IsActive);

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create([FromBody] UpsertReq req)
    {
        var code = (req.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length < 2) return BadRequest(new { error = "Invalid code" });

        var exists = await _db.DefectTypes.AnyAsync(x => x.Code == code);
        if (exists) return Conflict(new { error = "Defect type already exists" });

        var item = new DefectTypeDef
        {
            Code = code,
            Label = (req.Label ?? code).Trim(),
            Category = (req.Category ?? "OTHER").Trim().ToUpperInvariant(),
            SeverityDefault = Math.Clamp(req.SeverityDefault, 1, 3),
            SortOrder = req.SortOrder,
            IsActive = req.IsActive,
            CreatedBy = User?.Identity?.Name,
            Source = "ui"
        };

        _db.DefectTypes.Add(item);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPut("{code}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(string code, [FromBody] UpsertReq req)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        var item = await _db.DefectTypes.FirstOrDefaultAsync(x => x.Code == code);
        if (item is null) return NotFound();

        item.Label = (req.Label ?? item.Label).Trim();
        item.Category = (req.Category ?? item.Category).Trim().ToUpperInvariant();
        item.SeverityDefault = Math.Clamp(req.SeverityDefault, 1, 3);
        item.SortOrder = req.SortOrder;
        item.IsActive = req.IsActive;
        item.UpdatedAtUtc = DateTime.UtcNow;
        item.UpdatedBy = User?.Identity?.Name;
        item.Source = "ui";

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

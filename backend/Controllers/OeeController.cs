using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("oee")]
[Authorize(Policy = "OperatorOrAdmin")]
public class OeeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OeeService _oee;
    public OeeController(AppDbContext db, OeeService oee) { _db = db; _oee = oee; }

    [HttpGet("live")]
    public async Task<IActionResult> Live([FromQuery] int windowMinutes = 15)
    {
        windowMinutes = Math.Clamp(windowMinutes, 5, 120);

        var run = await _db.Runs.AsNoTracking().Include(r => r.Product)
            .Where(x => x.Status == RunStatus.Running || x.Status == RunStatus.Draining)
            .OrderByDescending(x => x.StartUtc)
            .FirstOrDefaultAsync();

        if (run is null) return Ok(new { running = false });

        var now = DateTime.UtcNow;
        var windowFrom = now.AddMinutes(-windowMinutes);
        var plannedEnd = run.ProductionEndUtc ?? (run.EndUtc ?? now);

        // For "live", compute in a sliding window but respect production end.
        var from = windowFrom < run.StartUtc ? run.StartUtc : windowFrom;
        var toPlanned = plannedEnd < now ? plannedEnd : now;
        if (toPlanned < from) toPlanned = from;

        var result = await _oee.ComputeWindow(run.Id, fromUtc: from, plannedEndUtc: toPlanned, countsEndUtc: run.EndUtc ?? now);
        return Ok(result);
    }

    [HttpGet("run/{runId:guid}")]
    public async Task<IActionResult> Run(Guid runId, [FromQuery] int bucketMinutes = 10)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);
        var result = await _oee.ComputeRun(runId, bucketMinutes);
        return Ok(result);
    }
}

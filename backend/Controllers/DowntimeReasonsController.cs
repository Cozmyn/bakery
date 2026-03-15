using Bakery.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("downtime-reasons")]
[Authorize(Policy = "OperatorOrAdmin")]
public class DowntimeReasonsController : ControllerBase
{
    private readonly AppDbContext _db;
    public DowntimeReasonsController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.DowntimeReasons.AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Code, x.Label, x.Category, x.IsOneTap })
            .ToListAsync();
        return Ok(items);
    }
}

using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("sim")]
[Authorize(Policy = "AdminOnly")]
public class SimulatorController : ControllerBase
{
    private readonly SimulatorManager _sim;

    public SimulatorController(SimulatorManager sim) { _sim = sim; }

    public record StartSimRequest(Guid ProductId);

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartSimRequest req)
    {
        var actor = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "admin";
        var (ok, message, runId) = await _sim.StartAsync(req.ProductId, actor);
        if (!ok) return BadRequest(new { error = message, runId });
        return Ok(new { message, runId });
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        var actor = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value ?? "admin";
        var (ok, message) = _sim.Stop(actor);
        if (!ok) return BadRequest(new { error = message });
        return Ok(new { message });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new { running = _sim.IsRunning, runId = _sim.CurrentRunId });
    }
}

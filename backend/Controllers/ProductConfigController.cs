using Bakery.Api.Data;
using Bakery.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("products/{productId:guid}")]
[Authorize(Policy = "AdminOnly")]
public class ProductConfigController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductConfigController(AppDbContext db) { _db = db; }

    public record DensityDefaultsDto(decimal DensityP1, decimal DensityP2, decimal DensityP3);

    [HttpPut("density-defaults")]
    public async Task<IActionResult> UpsertDensities(Guid productId, [FromBody] DensityDefaultsDto dto)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p is null) return NotFound();

        var existing = await _db.ProductDensityDefaults.FirstOrDefaultAsync(x => x.ProductId == productId);
        if (existing is null)
        {
            _db.ProductDensityDefaults.Add(new ProductDensityDefaults
            {
                ProductId = productId,
                DensityP1_GPerCm3 = dto.DensityP1,
                DensityP2_GPerCm3 = dto.DensityP2,
                DensityP3_GPerCm3 = dto.DensityP3,
                CreatedBy = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value,
                Source = "ui"
            });
        }
        else
        {
            existing.DensityP1_GPerCm3 = dto.DensityP1;
            existing.DensityP2_GPerCm3 = dto.DensityP2;
            existing.DensityP3_GPerCm3 = dto.DensityP3;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.UpdatedBy = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value;
            existing.Source = "ui";
            existing.DataStamp = Guid.NewGuid().ToString("N");
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    public record ToleranceDto(PointCode Point,
        decimal? WidthMinMm, decimal? WidthMaxMm,
        decimal? LengthMinMm, decimal? LengthMaxMm,
        decimal? HeightMinMm, decimal? HeightMaxMm,
        decimal? VolumeMinMm3, decimal? VolumeMaxMm3,
        decimal? WeightMinG, decimal? WeightMaxG);

    [HttpPut("tolerances")]
    public async Task<IActionResult> ReplaceTolerances(Guid productId, [FromBody] List<ToleranceDto> items)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p is null) return NotFound();

        var existing = await _db.ProductTolerances.Where(x => x.ProductId == productId).ToListAsync();
        _db.ProductTolerances.RemoveRange(existing);

        var by = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value;

        foreach (var t in items)
        {
            _db.ProductTolerances.Add(new ProductTolerance
            {
                ProductId = productId,
                Point = t.Point,
                WidthMinMm = t.WidthMinMm,
                WidthMaxMm = t.WidthMaxMm,
                LengthMinMm = t.LengthMinMm,
                LengthMaxMm = t.LengthMaxMm,
                HeightMinMm = t.HeightMinMm,
                HeightMaxMm = t.HeightMaxMm,
                VolumeMinMm3 = t.VolumeMinMm3,
                VolumeMaxMm3 = t.VolumeMaxMm3,
                WeightMinG = t.WeightMinG,
                WeightMaxG = t.WeightMaxG,
                CreatedBy = by,
                Source = "ui"
            });
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    public record SegmentDto(int SegmentId, decimal LengthM, decimal TargetSpeedMps);

    [HttpPut("segments")]
    public async Task<IActionResult> ReplaceSegments(Guid productId, [FromBody] List<SegmentDto> items)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p is null) return NotFound();

        var existing = await _db.ProductSegments.Where(x => x.ProductId == productId).ToListAsync();
        _db.ProductSegments.RemoveRange(existing);

        var by = User.Claims.FirstOrDefault(c => c.Type.Contains("email"))?.Value;

        foreach (var s in items)
        {
            _db.ProductSegments.Add(new ProductSegment
            {
                ProductId = productId,
                SegmentId = s.SegmentId,
                LengthM = s.LengthM,
                TargetSpeedMps = s.TargetSpeedMps,
                CreatedBy = by,
                Source = "ui"
            });
        }

        await _db.SaveChangesAsync();
        return Ok();
    }
}

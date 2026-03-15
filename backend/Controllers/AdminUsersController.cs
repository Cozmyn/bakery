using Bakery.Api.Data;
using Bakery.Api.Models;
using Bakery.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Api.Controllers;

[ApiController]
[Route("admin/users")]
[Authorize(Policy = "AdminOnly")]
public class AdminUsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminUsersController(AppDbContext db) => _db = db;

    public record CreateUserRequest(string Email, string Role, string? Password);
    public record UpdateUserRequest(string? Role, bool? IsActive, string? Password);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = true)
    {
        var q = _db.Users.AsNoTracking();
        if (!includeInactive) q = q.Where(x => x.IsActive);

        var users = await q
            .OrderBy(x => x.Email)
            .Select(x => new
            {
                x.Id,
                x.Email,
                role = x.Role.ToString(),
                x.IsActive,
                x.CreatedAtUtc,
                x.CreatedBy,
                x.UpdatedAtUtc,
                x.UpdatedBy
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            return BadRequest(new { error = "Invalid email" });

        if (await _db.Users.AnyAsync(x => x.Email.ToLower() == email))
            return Conflict(new { error = "Email already exists" });

        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            return BadRequest(new { error = "Invalid role" });

        var password = string.IsNullOrWhiteSpace(req.Password) ? PasswordService.GenerateTemporary(12) : req.Password!;
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordService.Hash(password),
            Role = role,
            IsActive = true,
            CreatedBy = User.Identity?.Name,
            Source = "admin"
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Return temporary password only if it was generated server-side.
        var temp = string.IsNullOrWhiteSpace(req.Password) ? password : null;

        return Ok(new
        {
            user.Id,
            user.Email,
            role = user.Role.ToString(),
            user.IsActive,
            temporaryPassword = temp
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (user is null) return NotFound();

        if (req.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
                return BadRequest(new { error = "Invalid role" });
            user.Role = role;
        }

        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;

        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = PasswordService.Hash(req.Password);

        user.UpdatedAtUtc = DateTime.UtcNow;
        user.UpdatedBy = User.Identity?.Name;
        user.Source = "admin";
        user.DataStamp = $"user:{Guid.NewGuid()}";

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

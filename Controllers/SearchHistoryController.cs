using System.Security.Claims;
using DIP.Backend.Data;
using DIP.Backend.Models;
using DIP.Backend.Models.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DIP.Backend.Controllers;

[ApiController]
[Route("api/search-history")]
[Authorize]
public class SearchHistoryController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SearchHistoryController(ApplicationDbContext db)
    {
        _db = db;
    }

    private int GetUserId()
    {
        return int.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "0"
        );
    }

    [HttpGet]
    public async Task<ActionResult<SearchHistoryPage>> GetRecent(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 5,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        take = Math.Clamp(take, 1, 20);
        skip = Math.Max(skip, 0);

        var query = _db.SearchHistories
            .Where(sh => sh.UserId == userId)
            .OrderByDescending(sh => sh.SearchedAt);

        var total = await query.CountAsync(ct);

        var entries = await query
            .Skip(skip)
            .Take(take)
            .Select(sh => new SearchHistoryListItem
            {
                Id = sh.Id,
                Query = sh.Query,
                SearchedAt = sh.SearchedAt,
                ResultCount = sh.SearchHistoryLiteratures.Count
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(new SearchHistoryPage
        {
            Items = entries,
            HasMore = skip + take < total
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SearchHistoryDetail>> GetById(int id, CancellationToken ct)
    {
        var userId = GetUserId();

        var entry = await _db.SearchHistories
            .Where(sh => sh.Id == id && sh.UserId == userId)
            .Select(sh => new SearchHistoryDetail
            {
                Id = sh.Id,
                Query = sh.Query,
                SearchedAt = sh.SearchedAt,
                Results = sh.SearchHistoryLiteratures
                    .Select(shl => shl.Literature)
                    .ToList()
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (entry == null)
            return NotFound(new { message = "Search history entry not found" });

        return Ok(entry);
    }

    [HttpPost]
    public async Task<ActionResult<SearchHistoryListItem>> Create(
        [FromBody] CreateSearchHistoryRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return BadRequest(new { message = "Query is required" });

        var userId = GetUserId();

        var validIds = await _db.Literature
            .Where(l => req.LiteratureIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(ct);

        var entry = new SearchHistory
        {
            UserId = userId,
            Query = req.Query.Trim(),
            SearchedAt = DateTime.UtcNow,
            SearchHistoryLiteratures = validIds
                .Select(lid => new SearchHistoryLiterature { LiteratureId = lid })
                .ToList()
        };

        _db.SearchHistories.Add(entry);
        await _db.SaveChangesAsync(ct);

        return Ok(new SearchHistoryListItem
        {
            Id = entry.Id,
            Query = entry.Query,
            SearchedAt = entry.SearchedAt,
            ResultCount = validIds.Count
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var userId = GetUserId();

        var entry = await _db.SearchHistories
            .FirstOrDefaultAsync(sh => sh.Id == id && sh.UserId == userId, ct);

        if (entry == null)
            return NotFound(new { message = "Search history entry not found" });

        _db.SearchHistories.Remove(entry);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Deleted" });
    }
}

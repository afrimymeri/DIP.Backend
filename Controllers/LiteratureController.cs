using DIP.Backend.Data;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using DIP.Backend.Models.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DIP.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LiteratureController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILiteratureScraperService _scraper;

    public LiteratureController(ApplicationDbContext db, ILiteratureScraperService scraper)
    {
        _db = db;
        _scraper = scraper;
    }
    
    [HttpPost("search")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<Literature>>> Search([FromBody] SearchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
        {
            return BadRequest(new { message = "Query is required" });
        }

        var results = await _scraper.SearchAsync(req.Query, req.Sources, Math.Clamp(req.Limit, 1, 50), ct);

        if (!req.Persist)
        {
            return Ok(results);
        }

        var saved = new List<Literature>();
        foreach (var r in results)
        {
            // Try find existing by DOI first, else by Source+ExternalId, else by Title+Year
            Literature? existing = null;
            if (!string.IsNullOrWhiteSpace(r.Doi))
            {
                existing = await _db.Literature.FirstOrDefaultAsync(l => l.Doi == r.Doi, ct);
            }
            if (existing == null && !string.IsNullOrWhiteSpace(r.ExternalId))
            {
                existing = await _db.Literature.FirstOrDefaultAsync(l => l.Source == r.Source && l.ExternalId == r.ExternalId, ct);
            }
            if (existing == null)
            {
                existing = await _db.Literature.FirstOrDefaultAsync(l => l.Title == r.Title && l.Year == r.Year, ct);
            }

            if (existing == null)
            {
                r.CreatedAt = DateTime.UtcNow;
                r.UpdatedAt = DateTime.UtcNow;
                _db.Literature.Add(r);
                saved.Add(r);
            }
            else
            {
                // Update a few mutable fields if they are missing on existing
                bool changed = false;
                if (string.IsNullOrWhiteSpace(existing.Abstract) && !string.IsNullOrWhiteSpace(r.Abstract)) { existing.Abstract = r.Abstract; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.Url) && !string.IsNullOrWhiteSpace(r.Url)) { existing.Url = r.Url; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.PdfUrl) && !string.IsNullOrWhiteSpace(r.PdfUrl)) { existing.PdfUrl = r.PdfUrl; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.Authors) && !string.IsNullOrWhiteSpace(r.Authors)) { existing.Authors = r.Authors; changed = true; }
                if (string.IsNullOrWhiteSpace(existing.Doi) && !string.IsNullOrWhiteSpace(r.Doi)) { existing.Doi = r.Doi; changed = true; }
                if (changed)
                {
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                saved.Add(existing);
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(saved);
    }
}

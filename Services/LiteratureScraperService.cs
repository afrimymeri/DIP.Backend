using System.Net.Http.Headers;
using System.Text.Json;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using DIP.Backend.Services.Scrapers;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services;

public class LiteratureScraperService : ILiteratureScraperService
{
    private readonly HttpClient _http;
    private readonly ILogger<LiteratureScraperService> _logger;
    private readonly Dictionary<LiteratureSource, ILiteratureScraper> _scrapers;

    public LiteratureScraperService(
        HttpClient http,
        ILogger<LiteratureScraperService> logger,
        IEnumerable<ILiteratureScraper> scrapers)
    {
        _http = http;
        _logger = logger;
        _scrapers = scrapers.ToDictionary(s => s.Source);

        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DIP-LiteratureSearch/1.0 (Academic Research Tool; mailto:contact@example.com)");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var available = _scrapers.Values.Where(s => s.IsAvailable).Select(s => s.Source);
        _logger.LogInformation("Available literature scrapers: {Scrapers}", string.Join(", ", available));
    }

    public async Task<IReadOnlyList<Literature>> SearchAsync(
        string query,
        IEnumerable<LiteratureSource>? sources = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        var srcs = sources?.ToList() ?? new List<LiteratureSource>
        {
            LiteratureSource.SemanticScholar,
            LiteratureSource.DBLP,
            LiteratureSource.OpenAlex,
            LiteratureSource.CrossRef
        };

        // Run all sources concurrently 
        var tasks = srcs.Distinct().Select(s => SearchSourceAsync(s, query, limit, ct));
        var resultsArrays = await Task.WhenAll(tasks);
        var results = resultsArrays.SelectMany(r => r).ToList();

        // Deduplicate by DOI if present else by Source+ExternalId else by Title+Year
        var distinct = results
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Doi)
                ? (string.IsNullOrWhiteSpace(r.ExternalId)
                    ? $"{r.Source}:{NormalizeTitle(r.Title)}:{r.Year}"
                    : $"{r.Source}:{r.ExternalId}")
                : r.Doi.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .Take(limit)
            .ToList();

        return distinct;
    }

    private async Task<IReadOnlyList<Literature>> SearchSourceAsync(
        LiteratureSource source,
        string query,
        int limit,
        CancellationToken ct)
    {
        // Delegate to individual scraper if available
        if (_scrapers.TryGetValue(source, out var scraper) && scraper.IsAvailable)
        {
            try
            {
                return await scraper.SearchAsync(query, limit, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search {Source} for query '{Query}'", source, query);
                return Array.Empty<Literature>();
            }
        }

        // Fallback to built-in implementations for sources not yet migrated
        try
        {
            return source switch
            {
                LiteratureSource.DBLP => await SearchDblpAsync(query, limit, ct),
                _ => Array.Empty<Literature>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search {Source} for query '{Query}'", source, query);
            return Array.Empty<Literature>();
        }
    }

    private static string NormalizeTitle(string title) =>
        title.ToLowerInvariant().Trim();
    [Obsolete("This currently doesnt work as DBLP is currently down")]
    private async Task<IReadOnlyList<Literature>> SearchDblpAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"https://dblp.org/search/publ/api?q={Uri.EscapeDataString(query)}&h={Math.Min(limit, 100)}&format=json";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = doc.RootElement.GetProperty("result");
        if (!result.TryGetProperty("hits", out var hitsWrapper) ||
            !hitsWrapper.TryGetProperty("hit", out var hits))
            return Array.Empty<Literature>();

        var list = new List<Literature>();
        foreach (var hit in hits.EnumerateArray())
        {
            var info = hit.GetProperty("info");
            var lit = new Literature
            {
                Title = info.GetPropertyOrDefault("title")?.GetString() ?? string.Empty,
                Url = info.GetPropertyOrDefault("url")?.GetString(),
                Year = info.GetPropertyOrDefault("year")?.GetString(),
                Doi = info.GetPropertyOrDefault("doi")?.GetString(),
                Source = LiteratureSource.DBLP,
                ExternalId = info.GetPropertyOrDefault("key")?.GetString()
            };

            if (info.TryGetProperty("authors", out var authorsEl) &&
                authorsEl.TryGetProperty("author", out var authorArr))
            {
                var authors = authorArr.ValueKind == JsonValueKind.Array
                    ? authorArr.EnumerateArray().Select(a => a.GetPropertyOrDefault("text")?.GetString())
                    : new[] { authorArr.GetPropertyOrDefault("text")?.GetString() };
                lit.Authors = string.Join(", ", authors!.Where(a => !string.IsNullOrWhiteSpace(a)));
            }

            list.Add(lit);
        }

        _logger.LogInformation("DBLP returned {Count} results for '{Query}'", list.Count, query);
        return list;
    }
}

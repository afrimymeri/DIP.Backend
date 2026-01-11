using System.Text.Json;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DIP.Backend.Services.Scrapers;

/// <summary>
/// Literature scraper for IEEE Xplore database.
/// Docs: https://developer.ieee.org/docs
/// </summary>
public class IEEEXploreScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://ieeexploreapi.ieee.org/api/v1/search/articles";
    private const int MaxResultsPerQuery = 200;

    private readonly string? _apiKey;

    public override LiteratureSource Source => LiteratureSource.IEEEXplore;
    public override bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    public IEEEXploreScraper(
        HttpClient http,
        ILogger<IEEEXploreScraper> logger,
        IOptions<LiteratureApiKeysOptions> apiKeysOptions)
        : base(http, logger)
    {
        _apiKey = apiKeysOptions.Value.IEEEXplore;

        if (!IsAvailable)
        {
            Logger.LogWarning("IEEE Xplore API key not configured. This source will be unavailable.");
        }
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            Logger.LogWarning("IEEE Xplore search skipped - API key not configured");
            return Array.Empty<Literature>();
        }

        try
        {
            var maxResults = Math.Min(limit, MaxResultsPerQuery);
            var url = $"{BaseUrl}?querytext={Uri.EscapeDataString(query)}&max_records={maxResults}&apikey={_apiKey}";

            using var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var results = ParseResponse(doc.RootElement);
            LogResults(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogError(ex, query);
            return Array.Empty<Literature>();
        }
    }

    private List<Literature> ParseResponse(JsonElement root)
    {
        var list = new List<Literature>();

        // Check if we have articles in the response
        if (!root.TryGetProperty("articles", out var articles))
        {
            return list;
        }

        foreach (var article in articles.EnumerateArray())
        {
            var lit = new Literature
            {
                Title = article.GetPropertyOrDefault("title")?.GetString()?.Trim() ?? string.Empty,
                Abstract = article.GetPropertyOrDefault("abstract")?.GetString()?.Trim(),
                Source = LiteratureSource.IEEEXplore
            };

            // External ID (article number is IEEE's unique identifier)
            lit.ExternalId = article.GetPropertyOrDefault("article_number")?.GetString();

            // DOI
            lit.Doi = article.GetPropertyOrDefault("doi")?.GetString();

            // Publication year
            if (article.TryGetProperty("publication_year", out var yearEl))
            {
                lit.Year = yearEl.ValueKind == JsonValueKind.Number
                    ? yearEl.GetInt32().ToString()
                    : yearEl.GetString();
            }

            // Authors - can be an array of objects with full_name
            if (article.TryGetProperty("authors", out var authorsEl) &&
                authorsEl.TryGetProperty("authors", out var authorsArray))
            {
                var authors = authorsArray.EnumerateArray()
                    .Select(a => a.GetPropertyOrDefault("full_name")?.GetString())
                    .Where(n => !string.IsNullOrWhiteSpace(n));
                lit.Authors = string.Join(", ", authors!);
            }

            // URLs
            lit.PdfUrl = article.GetPropertyOrDefault("pdf_url")?.GetString();
            lit.Url = article.GetPropertyOrDefault("html_url")?.GetString();

            // Fallback URL using article number
            if (string.IsNullOrEmpty(lit.Url) && !string.IsNullOrEmpty(lit.ExternalId))
            {
                lit.Url = $"https://ieeexplore.ieee.org/document/{lit.ExternalId}";
            }

            if (!string.IsNullOrWhiteSpace(lit.Title))
            {
                list.Add(lit);
            }
        }

        return list;
    }
}

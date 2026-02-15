using System.Text.Json;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services.Scrapers;

/// <summary>
/// Literature scraper for OpenAlex.
/// Docs: https://docs.openalex.org/api-entities/works/search-works
/// </summary>
public class OpenAlexScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://api.openalex.org/works";
    private const int MaxResultsPerQuery = 100;

    public override LiteratureSource Source => LiteratureSource.OpenAlex;
    public override bool IsAvailable => true;

    public OpenAlexScraper(
        HttpClient http,
        ILogger<OpenAlexScraper> logger)
        : base(http, logger)
    {
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}?search={Uri.EscapeDataString(query)}&per_page={Math.Min(limit, MaxResultsPerQuery)}";

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

        if (!root.TryGetProperty("results", out var results))
            return list;

        foreach (var item in results.EnumerateArray())
        {
            var lit = new Literature
            {
                Title = item.GetPropertyOrDefault("title")?.GetString() ?? string.Empty,
                Source = LiteratureSource.OpenAlex,
                ExternalId = item.GetPropertyOrDefault("id")?.GetString()?.Replace("https://openalex.org/", "")
            };

            if (item.TryGetProperty("doi", out var doiEl) && doiEl.ValueKind == JsonValueKind.String)
            {
                lit.Doi = doiEl.GetString()?.Replace("https://doi.org/", "");
            }

            if (item.TryGetProperty("publication_year", out var yearEl))
            {
                lit.Year = yearEl.GetRawText();
            }

            if (item.TryGetProperty("abstract_inverted_index", out var abstractEl) &&
                abstractEl.ValueKind == JsonValueKind.Object)
            {
                lit.Abstract = ReconstructAbstract(abstractEl);
            }

            if (item.TryGetProperty("authorships", out var authorships))
            {
                var authors = authorships.EnumerateArray()
                    .Select(a => a.GetPropertyOrDefault("author")?.GetPropertyOrDefault("display_name")?.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                lit.Authors = string.Join(", ", authors!);
            }

            if (item.TryGetProperty("open_access", out var oaEl))
            {
                lit.PdfUrl = oaEl.GetPropertyOrDefault("oa_url")?.GetString();
            }
            if (item.TryGetProperty("primary_location", out var locEl))
            {
                lit.Url = locEl.GetPropertyOrDefault("landing_page_url")?.GetString();
                if (string.IsNullOrEmpty(lit.PdfUrl))
                {
                    lit.PdfUrl = locEl.GetPropertyOrDefault("pdf_url")?.GetString();
                }
            }

            list.Add(lit);
        }

        return list;
    }

    private static string? ReconstructAbstract(JsonElement invertedIndex)
    {
        try
        {
            var wordPositions = new List<(int position, string word)>();
            foreach (var prop in invertedIndex.EnumerateObject())
            {
                foreach (var pos in prop.Value.EnumerateArray())
                {
                    wordPositions.Add((pos.GetInt32(), prop.Name));
                }
            }
            return string.Join(" ", wordPositions.OrderBy(x => x.position).Select(x => x.word));
        }
        catch
        {
            return null;
        }
    }
}

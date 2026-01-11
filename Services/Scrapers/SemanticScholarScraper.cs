using System.Text.Json;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DIP.Backend.Services.Scrapers;

/// Docs: https://api.semanticscholar.org/api-docs/
public class SemanticScholarScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://api.semanticscholar.org/graph/v1/paper/search";
    private const int MaxResultsPerQuery = 100;

    private readonly string? _apiKey;

    public override LiteratureSource Source => LiteratureSource.SemanticScholar;

    public override bool IsAvailable => true;

    public SemanticScholarScraper(
        HttpClient http,
        ILogger<SemanticScholarScraper> logger,
        IOptions<LiteratureApiKeysOptions> apiKeysOptions)
        : base(http, logger)
    {
        _apiKey = apiKeysOptions.Value.SemanticScholar;

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            Logger.LogWarning("Semantic Scholar API key not configured. Using free tier with strict rate limits.");
        }
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        try
        {
            var maxResults = Math.Min(limit, MaxResultsPerQuery);
            var url = $"{BaseUrl}?query={Uri.EscapeDataString(query)}&limit={maxResults}&fields=title,abstract,year,externalIds,authors,url,openAccessPdf";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                request.Headers.Add("x-api-key", _apiKey);
            }

            using var response = await Http.SendAsync(request, ct);
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

        if (!root.TryGetProperty("data", out var data))
            return list;

        foreach (var item in data.EnumerateArray())
        {
            var lit = new Literature
            {
                Title = item.GetPropertyOrDefault("title")?.GetString() ?? string.Empty,
                Abstract = item.GetPropertyOrDefault("abstract")?.GetString(),
                Year = item.GetPropertyOrDefault("year")?.GetRawText(),
                Url = item.GetPropertyOrDefault("url")?.GetString(),
                Source = LiteratureSource.SemanticScholar
            };
            
            if (item.TryGetProperty("authors", out var authorsEl))
            {
                var authors = authorsEl.EnumerateArray()
                    .Select(a => a.GetPropertyOrDefault("name")?.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                lit.Authors = string.Join(", ", authors!);
            }
            
            if (item.TryGetProperty("openAccessPdf", out var pdfEl) && pdfEl.ValueKind == JsonValueKind.Object)
            {
                lit.PdfUrl = pdfEl.GetPropertyOrDefault("url")?.GetString();
            }
            
            if (item.TryGetProperty("externalIds", out var extEl) && extEl.ValueKind == JsonValueKind.Object)
            {
                lit.Doi = extEl.GetPropertyOrDefault("DOI")?.GetString();
                lit.ExternalId = item.GetPropertyOrDefault("paperId")?.GetString()
                    ?? extEl.GetPropertyOrDefault("CorpusId")?.GetRawText();
            }

            if (!string.IsNullOrWhiteSpace(lit.Title))
            {
                list.Add(lit);
            }
        }

        return list;
    }
}

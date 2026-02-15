using System.Text.Json;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services.Scrapers;

/// <summary>
/// Literature scraper for CrossRef.
/// Docs: https://api.crossref.org/swagger-ui/index.html
/// </summary>
public class CrossRefScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://api.crossref.org/works";
    private const int MaxResultsPerQuery = 100;

    public override LiteratureSource Source => LiteratureSource.CrossRef;
    public override bool IsAvailable => true;

    public CrossRefScraper(
        HttpClient http,
        ILogger<CrossRefScraper> logger)
        : base(http, logger)
    {
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}?query={Uri.EscapeDataString(query)}&rows={Math.Min(limit, MaxResultsPerQuery)}";

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

        if (!root.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("items", out var items))
            return list;

        foreach (var item in items.EnumerateArray())
        {
            var lit = new Literature
            {
                Source = LiteratureSource.CrossRef,
                Doi = item.GetPropertyOrDefault("DOI")?.GetString(),
                Url = item.GetPropertyOrDefault("URL")?.GetString()
            };

            lit.ExternalId = lit.Doi;

            if (item.TryGetProperty("title", out var titleArr) && titleArr.ValueKind == JsonValueKind.Array)
            {
                var titles = titleArr.EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrEmpty(t));
                lit.Title = string.Join(" ", titles!);
            }

            lit.Abstract = item.GetPropertyOrDefault("abstract")?.GetString();

            if (item.TryGetProperty("published-print", out var pubPrint) &&
                pubPrint.TryGetProperty("date-parts", out var dateParts))
            {
                var year = dateParts.EnumerateArray().FirstOrDefault().EnumerateArray().FirstOrDefault();
                if (year.ValueKind == JsonValueKind.Number)
                    lit.Year = year.GetInt32().ToString();
            }
            else if (item.TryGetProperty("published-online", out var pubOnline) &&
                     pubOnline.TryGetProperty("date-parts", out var onlineDateParts))
            {
                var year = onlineDateParts.EnumerateArray().FirstOrDefault().EnumerateArray().FirstOrDefault();
                if (year.ValueKind == JsonValueKind.Number)
                    lit.Year = year.GetInt32().ToString();
            }

            if (item.TryGetProperty("author", out var authors))
            {
                var authorNames = authors.EnumerateArray()
                    .Select(a =>
                    {
                        var given = a.GetPropertyOrDefault("given")?.GetString() ?? "";
                        var family = a.GetPropertyOrDefault("family")?.GetString() ?? "";
                        return $"{given} {family}".Trim();
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                lit.Authors = string.Join(", ", authorNames);
            }

            if (item.TryGetProperty("link", out var links))
            {
                var pdfLink = links.EnumerateArray()
                    .FirstOrDefault(l => l.GetPropertyOrDefault("content-type")?.GetString()?.Contains("pdf") == true);
                if (pdfLink.ValueKind == JsonValueKind.Object)
                {
                    lit.PdfUrl = pdfLink.GetPropertyOrDefault("URL")?.GetString();
                }
            }

            if (!string.IsNullOrWhiteSpace(lit.Title))
                list.Add(lit);
        }

        return list;
    }
}

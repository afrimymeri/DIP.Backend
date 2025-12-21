using System.Net.Http.Headers;
using System.Text.Json;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;

namespace DIP.Backend.Services;

public class LiteratureScraperService(HttpClient http) : ILiteratureScraperService
{
    private readonly HttpClient _http = http;

    public async Task<IReadOnlyList<Literature>> SearchAsync(string query, IEnumerable<LiteratureSource>? sources = null, int limit = 20, CancellationToken ct = default)
    {
        var srcs = (sources?.ToList() ?? new List<LiteratureSource> { LiteratureSource.SemanticScholar, LiteratureSource.DBLP });
        var results = new List<Literature>();
        foreach (var s in srcs.Distinct())
        {
            switch (s)
            {
                case LiteratureSource.SemanticScholar:
                    results.AddRange(await SearchSemanticScholarAsync(query, limit, ct));
                    break;
                case LiteratureSource.DBLP:
                    results.AddRange(await SearchDblpAsync(query, limit, ct));
                    break;
                default:
                    break;
            }
        }

        // Deduplicate by DOI if present, else by Source+ExternalId, else by Title+Year
        var distinct = results
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Doi)
                ? (string.IsNullOrWhiteSpace(r.ExternalId) ? $"{r.Source}:{r.Title}:{r.Year}" : $"{r.Source}:{r.ExternalId}")
                : r.Doi.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .Take(limit)
            .ToList();

        return distinct;
    }

    private async Task<IReadOnlyList<Literature>> SearchSemanticScholarAsync(string query, int limit, CancellationToken ct)
    {
        // Docs: https://api.semanticscholar.org/api-docs/graph#tag/Paper-Data/operation/get_graph_v1_paper_search
        var url = $"https://api.semanticscholar.org/graph/v1/paper/search?query={Uri.EscapeDataString(query)}&limit={Math.Min(limit, 25)}&fields=title,abstract,year,externalIds,authors,url,openAccessPdf";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var data = doc.RootElement.GetProperty("data");
        var list = new List<Literature>();
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
                var authors = authorsEl.EnumerateArray().Select(a => a.GetPropertyOrDefault("name")?.GetString()).Where(s => !string.IsNullOrWhiteSpace(s));
                lit.Authors = string.Join(", ", authors!);
            }
            if (item.TryGetProperty("openAccessPdf", out var pdfEl) && pdfEl.ValueKind == JsonValueKind.Object)
            {
                lit.PdfUrl = pdfEl.GetPropertyOrDefault("url")?.GetString();
            }
            if (item.TryGetProperty("externalIds", out var extEl) && extEl.ValueKind == JsonValueKind.Object)
            {
                lit.Doi = extEl.GetPropertyOrDefault("DOI")?.GetString();
                // Semantic Scholar paperId as ExternalId
                lit.ExternalId = item.GetPropertyOrDefault("paperId")?.GetString() ?? extEl.GetPropertyOrDefault("CorpusId")?.GetRawText();
            }
            list.Add(lit);
        }
        return list;
    }

    private async Task<IReadOnlyList<Literature>> SearchDblpAsync(string query, int limit, CancellationToken ct)
    {
        // Docs: https://dblp.org/faq/13501473.html
        var url = $"https://dblp.org/search/publ/api?q={Uri.EscapeDataString(query)}&h={Math.Min(limit, 25)}&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var hits = doc.RootElement.GetProperty("result").GetProperty("hits").GetProperty("hit");
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
            if (info.TryGetProperty("authors", out var authorsEl) && authorsEl.TryGetProperty("author", out var authorArr))
            {
                var authors = authorArr.ValueKind == JsonValueKind.Array
                    ? authorArr.EnumerateArray().Select(a => a.GetPropertyOrDefault("text")?.GetString())
                    : new[] { authorArr.GetPropertyOrDefault("text")?.GetString() };
                lit.Authors = string.Join(", ", authors!.Where(a => !string.IsNullOrWhiteSpace(a)));
            }
            list.Add(lit);
        }
        return list;
    }
}

file static class JsonExtensions
{
    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }
}

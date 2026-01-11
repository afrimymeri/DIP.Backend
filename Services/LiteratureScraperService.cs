using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
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

        // Log available scrapers
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
                LiteratureSource.SemanticScholar => await SearchSemanticScholarAsync(query, limit, ct),
                LiteratureSource.DBLP => await SearchDblpAsync(query, limit, ct),
                LiteratureSource.OpenAlex => await SearchOpenAlexAsync(query, limit, ct),
                LiteratureSource.CrossRef => await SearchCrossRefAsync(query, limit, ct),
                LiteratureSource.ArXiv => await SearchArXivAsync(query, limit, ct),
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

    private async Task<IReadOnlyList<Literature>> SearchSemanticScholarAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"https://api.semanticscholar.org/graph/v1/paper/search?query={Uri.EscapeDataString(query)}&limit={Math.Min(limit, 100)}&fields=title,abstract,year,externalIds,authors,url,openAccessPdf";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("data", out var data))
            return Array.Empty<Literature>();

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

            list.Add(lit);
        }

        _logger.LogInformation("SemanticScholar returned {Count} results for '{Query}'", list.Count, query);
        return list;
    }

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

    private async Task<IReadOnlyList<Literature>> SearchOpenAlexAsync(string query, int limit, CancellationToken ct)
    {
        // Docs: https://docs.openalex.org/api-entities/works/search-works
        var url = $"https://api.openalex.org/works?search={Uri.EscapeDataString(query)}&per_page={Math.Min(limit, 100)}";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return Array.Empty<Literature>();

        var list = new List<Literature>();
        foreach (var item in results.EnumerateArray())
        {
            var lit = new Literature
            {
                Title = item.GetPropertyOrDefault("title")?.GetString() ?? string.Empty,
                Source = LiteratureSource.OpenAlex,
                ExternalId = item.GetPropertyOrDefault("id")?.GetString()?.Replace("https://openalex.org/", "")
            };

            // DOI
            if (item.TryGetProperty("doi", out var doiEl) && doiEl.ValueKind == JsonValueKind.String)
            {
                lit.Doi = doiEl.GetString()?.Replace("https://doi.org/", "");
            }

            // Year
            if (item.TryGetProperty("publication_year", out var yearEl))
            {
                lit.Year = yearEl.GetRawText();
            }

            // Abstract (OpenAlex provides inverted index, a reconstruct is needed)
            if (item.TryGetProperty("abstract_inverted_index", out var abstractEl) &&
                abstractEl.ValueKind == JsonValueKind.Object)
            {
                lit.Abstract = ReconstructAbstract(abstractEl);
            }

            // Authors
            if (item.TryGetProperty("authorships", out var authorships))
            {
                var authors = authorships.EnumerateArray()
                    .Select(a => a.GetPropertyOrDefault("author")?.GetPropertyOrDefault("display_name")?.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                lit.Authors = string.Join(", ", authors!);
            }

            // URL - prefer open access, fallback to DOI link
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

        _logger.LogInformation("OpenAlex returned {Count} results for '{Query}'", list.Count, query);
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

    private async Task<IReadOnlyList<Literature>> SearchCrossRefAsync(string query, int limit, CancellationToken ct)
    {
        // CrossRef is a DOI registration agency with extensive metadata
        // Docs: https://api.crossref.org/swagger-ui/index.html
        var url = $"https://api.crossref.org/works?query={Uri.EscapeDataString(query)}&rows={Math.Min(limit, 100)}";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("items", out var items))
            return Array.Empty<Literature>();

        var list = new List<Literature>();
        foreach (var item in items.EnumerateArray())
        {
            var lit = new Literature
            {
                Source = LiteratureSource.CrossRef,
                Doi = item.GetPropertyOrDefault("DOI")?.GetString(),
                Url = item.GetPropertyOrDefault("URL")?.GetString()
            };

            lit.ExternalId = lit.Doi;

            // Title is an array
            if (item.TryGetProperty("title", out var titleArr) && titleArr.ValueKind == JsonValueKind.Array)
            {
                var titles = titleArr.EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrEmpty(t));
                lit.Title = string.Join(" ", titles!);
            }

            // Abstract
            lit.Abstract = item.GetPropertyOrDefault("abstract")?.GetString();

            // Year from published print or published online
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

            // Authors
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

            // PDF link
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

        _logger.LogInformation("CrossRef returned {Count} results for '{Query}'", list.Count, query);
        return list;
    }

    private async Task<IReadOnlyList<Literature>> SearchArXivAsync(string query, int limit, CancellationToken ct)
    {
        // arXiv API returns Atom XML
        // Docs: https://info.arxiv.org/help/api/basics.html
        var url = $"https://export.arxiv.org/api/query?search_query=all:{Uri.EscapeDataString(query)}&start=0&max_results={Math.Min(limit, 100)}";

        using var res = await _http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();

        var content = await res.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(content);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace arxiv = "http://arxiv.org/schemas/atom";

        var list = new List<Literature>();
        foreach (var entry in doc.Descendants(atom + "entry"))
        {
            var lit = new Literature
            {
                Title = entry.Element(atom + "title")?.Value.Trim().Replace("\n", " "),
                Abstract = entry.Element(atom + "summary")?.Value.Trim().Replace("\n", " "),
                Url = entry.Element(atom + "id")?.Value,
                Source = LiteratureSource.ArXiv
            };

            var idUrl = entry.Element(atom + "id")?.Value;
            if (!string.IsNullOrEmpty(idUrl))
            {
                lit.ExternalId = idUrl.Replace("http://arxiv.org/abs/", "");
            }
            
            var published = entry.Element(atom + "published")?.Value;
            if (DateTime.TryParse(published, out var pubDate))
            {
                lit.Year = pubDate.Year.ToString();
            }

            var authors = entry.Elements(atom + "author")
                .Select(a => a.Element(atom + "name")?.Value)
                .Where(n => !string.IsNullOrWhiteSpace(n));
            lit.Authors = string.Join(", ", authors!);
            
            var pdfLink = entry.Elements(atom + "link")
                .FirstOrDefault(l => l.Attribute("title")?.Value == "pdf");
            lit.PdfUrl = pdfLink?.Attribute("href")?.Value;

            // DOI (if present in arxiv namespace)
            lit.Doi = entry.Element(arxiv + "doi")?.Value;

            if (!string.IsNullOrWhiteSpace(lit.Title))
                list.Add(lit);
        }

        _logger.LogInformation("arXiv returned {Count} results for '{Query}'", list.Count, query);
        return list;
    }
}

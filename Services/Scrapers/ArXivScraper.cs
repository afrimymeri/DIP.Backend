using System.Xml.Linq;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services.Scrapers;

/// <summary>
/// Literature scraper for arXiv.
/// Docs: https://info.arxiv.org/help/api/basics.html
/// </summary>
public class ArXivScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://export.arxiv.org/api/query";
    private const int MaxResultsPerQuery = 100;

    public override LiteratureSource Source => LiteratureSource.ArXiv;
    public override bool IsAvailable => true;

    public ArXivScraper(
        HttpClient http,
        ILogger<ArXivScraper> logger)
        : base(http, logger)
    {
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}?search_query=all:{Uri.EscapeDataString(query)}&start=0&max_results={Math.Min(limit, MaxResultsPerQuery)}";

            using var response = await Http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            var results = ParseResponse(content);
            LogResults(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogError(ex, query);
            return Array.Empty<Literature>();
        }
    }

    private List<Literature> ParseResponse(string xml)
    {
        var doc = XDocument.Parse(xml);

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

            lit.Doi = entry.Element(arxiv + "doi")?.Value;

            if (!string.IsNullOrWhiteSpace(lit.Title))
                list.Add(lit);
        }

        return list;
    }
}

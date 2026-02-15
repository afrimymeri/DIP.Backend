using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using DIP.Backend.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services.Scrapers;

/// <summary>
/// Literature scraper for ACM Digital Library.
/// Since ACM has no public API, this scraper parses HTML search results.
/// Note: ACM may block automated access after repeated requests.
/// </summary>
public class ACMDigitalLibraryScraper : BaseLiteratureScraper
{
    private const string BaseUrl = "https://dl.acm.org/action/doSearch";
    private const int MaxResultsPerQuery = 50;

    public override LiteratureSource Source => LiteratureSource.ACMDigitalLibrary;
    public override bool IsAvailable => true;

    public ACMDigitalLibraryScraper(
        HttpClient http,
        ILogger<ACMDigitalLibraryScraper> logger)
        : base(http, logger)
    {
        // Override Accept header: base sets application/json, but ACM returns HTML
        Http.DefaultRequestHeaders.Accept.Clear();
        Http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/html"));
        Http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/xhtml+xml", 0.9));

        // Browser-like headers to reduce chance of 403
        Http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        Http.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        Http.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

        // Override UserAgent to look more like a real browser
        Http.DefaultRequestHeaders.UserAgent.Clear();
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36");
    }

    public override async Task<IReadOnlyList<Literature>> SearchAsync(
        string query, int limit, CancellationToken ct = default)
    {
        try
        {
            // Visit the homepage first to establish session cookies
            using var sessionResponse = await Http.GetAsync("https://dl.acm.org/", ct);
            Logger.LogDebug("ACM session request returned {StatusCode}", sessionResponse.StatusCode);

            var pageSize = Math.Min(limit, MaxResultsPerQuery);
            var url = $"{BaseUrl}?AllField={Uri.EscapeDataString(query)}" +
                      $"&startPage=0&pageSize={pageSize}";

            // Now perform the actual search with session cookies
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://dl.acm.org/");

            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            var results = ParseSearchResults(html);
            LogResults(results.Count, query);
            return results;
        }
        catch (Exception ex)
        {
            LogError(ex, query);
            return Array.Empty<Literature>();
        }
    }

    private List<Literature> ParseSearchResults(string html)
    {
        var list = new List<Literature>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Each search result is an <li> with class "search__item"
        var resultNodes = doc.DocumentNode.SelectNodes(
            "//li[contains(@class, 'search__item')]");

        if (resultNodes == null)
            return list;

        foreach (var node in resultNodes)
        {
            var lit = new Literature
            {
                Source = LiteratureSource.ACMDigitalLibrary
            };

            // Title: <span class="hlFld-Title"><a href="/doi/...">Title Text</a></span>
            var titleNode = node.SelectSingleNode(
                ".//span[contains(@class, 'hlFld-Title')]/a");
            if (titleNode != null)
            {
                lit.Title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();

                var href = titleNode.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    lit.Url = $"https://dl.acm.org{href}";

                    // Extract DOI from href like "/doi/10.1145/1234567.1234568"
                    if (href.StartsWith("/doi/"))
                    {
                        lit.Doi = href.Substring("/doi/".Length);
                        lit.ExternalId = lit.Doi;
                    }
                }
            }

            // Authors: <ul aria-label="authors"><li><a><span>Author Name</span></a></li>...
            var authorNodes = node.SelectNodes(
                ".//ul[@aria-label='authors']//a/span");
            if (authorNodes != null)
            {
                var authors = authorNodes
                    .Select(a => HtmlEntity.DeEntitize(a.InnerText).Trim())
                    .Where(a => !string.IsNullOrWhiteSpace(a));
                lit.Authors = string.Join(", ", authors);
            }

            // Abstract: <div class="issue-item__abstract"><p>...</p></div>
            var abstractNode = node.SelectSingleNode(
                ".//div[contains(@class, 'issue-item__abstract')]//p");
            if (abstractNode != null)
            {
                lit.Abstract = HtmlEntity.DeEntitize(abstractNode.InnerText).Trim();
            }

            // Year: extract from <span class="dot-separator"> elements
            var dotSeparators = node.SelectNodes(
                ".//span[contains(@class, 'dot-separator')]");
            if (dotSeparators != null)
            {
                lit.Year = ExtractYear(dotSeparators);
            }

            // DOI fallback: <a class="issue-item__doi"> contains DOI text
            if (string.IsNullOrEmpty(lit.Doi))
            {
                var doiNode = node.SelectSingleNode(
                    ".//a[contains(@class, 'issue-item__doi')]");
                if (doiNode != null)
                {
                    var doiText = HtmlEntity.DeEntitize(doiNode.InnerText).Trim();
                    if (doiText.StartsWith("https://doi.org/"))
                        doiText = doiText.Replace("https://doi.org/", "");
                    lit.Doi = doiText;
                    lit.ExternalId ??= doiText;
                }
            }

            if (!string.IsNullOrWhiteSpace(lit.Title))
            {
                list.Add(lit);
            }
        }

        return list;
    }

    private static string? ExtractYear(HtmlNodeCollection dotSeparators)
    {
        foreach (var span in dotSeparators)
        {
            var text = HtmlEntity.DeEntitize(span.InnerText).Trim();
            var match = Regex.Match(text, @"\b(19|20)\d{2}\b");
            if (match.Success)
                return match.Value;
        }
        return null;
    }
}

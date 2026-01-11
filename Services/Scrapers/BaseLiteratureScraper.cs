using System.Net.Http.Headers;
using System.Text.Json;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using Microsoft.Extensions.Logging;

namespace DIP.Backend.Services.Scrapers;


public abstract class BaseLiteratureScraper : ILiteratureScraper
{
    protected readonly HttpClient Http;
    protected readonly ILogger Logger;

    public abstract LiteratureSource Source { get; }
    public abstract bool IsAvailable { get; }

    protected BaseLiteratureScraper(HttpClient http, ILogger logger)
    {
        Http = http;
        Logger = logger;

        Http.Timeout = TimeSpan.FromSeconds(30);
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DIP-LiteratureSearch/1.0 (Academic Research Tool; mailto:afrimymeri0@gmail.com)");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public abstract Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default);

    protected void LogResults(int count, string query)
    {
        Logger.LogInformation("{Source} returned {Count} results for '{Query}'", Source, count, query);
    }

    protected void LogError(Exception ex, string query)
    {
        Logger.LogWarning(ex, "Failed to search {Source} for query '{Query}'", Source, query);
    }
}

public static class JsonExtensions
{
    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }
}

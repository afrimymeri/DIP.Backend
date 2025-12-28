namespace DIP.Backend.Models;

public enum LiteratureSource
{
    // Implemented - Free APIs (no key required)
    SemanticScholar = 1,
    DBLP = 2,
    OpenAlex = 3,
    CrossRef = 4,
    ArXiv = 5,

    // Pending - Require API keys or scraping
    IEEEXplore = 10,        // Requires API key
    ACMDigitalLibrary = 11, // No public API
    ScienceDirect = 12,     // Requires Elsevier API key
    SpringerLink = 13,      // Requires API key
    GoogleScholar = 14      // No official API, blocks scrapers
}

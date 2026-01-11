using DIP.Backend.Models;

namespace DIP.Backend.Interfaces;


public interface ILiteratureScraper
{
    LiteratureSource Source { get; }
    
    bool IsAvailable { get; }
    
    Task<IReadOnlyList<Literature>> SearchAsync(string query, int limit, CancellationToken ct = default);
}

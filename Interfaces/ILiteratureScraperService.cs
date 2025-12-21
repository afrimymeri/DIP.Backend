using DIP.Backend.Models;

namespace DIP.Backend.Interfaces;

public interface ILiteratureScraperService
{
    Task<IReadOnlyList<Literature>> SearchAsync(string query, IEnumerable<LiteratureSource>? sources = null, int limit = 20, CancellationToken ct = default);
}

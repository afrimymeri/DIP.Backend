namespace DIP.Backend.Models;

public class SearchHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Query { get; set; } = string.Empty;
    public DateTime SearchedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SearchHistoryLiterature> SearchHistoryLiteratures { get; set; }
        = new List<SearchHistoryLiterature>();
}

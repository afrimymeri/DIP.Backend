namespace DIP.Backend.Models;

public class SearchHistoryLiterature
{
    public int SearchHistoryId { get; set; }
    public SearchHistory SearchHistory { get; set; } = null!;

    public int LiteratureId { get; set; }
    public Literature Literature { get; set; } = null!;
}

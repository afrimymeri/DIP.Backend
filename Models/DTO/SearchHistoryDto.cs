namespace DIP.Backend.Models.Dto;

public class CreateSearchHistoryRequest
{
    public string Query { get; set; } = string.Empty;
    public List<int> LiteratureIds { get; set; } = new();
}

public class SearchHistoryListItem
{
    public int Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public DateTime SearchedAt { get; set; }
    public int ResultCount { get; set; }
}

public class SearchHistoryPage
{
    public List<SearchHistoryListItem> Items { get; set; } = new();
    public bool HasMore { get; set; }
}

public class SearchHistoryDetail
{
    public int Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public DateTime SearchedAt { get; set; }
    public List<Literature> Results { get; set; } = new();
}

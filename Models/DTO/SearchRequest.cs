namespace DIP.Backend.Models.Dto;

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public List<LiteratureSource>? Sources { get; set; }
    public int Limit { get; set; } = 20;
    public bool Persist { get; set; } = true;
}
namespace DIP.Backend.Models;

public class LiteratureApiKeysOptions
{
    public const string SectionName = "LiteratureApiKeys";

    public string? IEEEXplore { get; set; }
    public string? SpringerLink { get; set; }
    public string? ScienceDirect { get; set; }
}

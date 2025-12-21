using System.ComponentModel.DataAnnotations;

namespace DIP.Backend.Models;

public class Literature
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Abstract { get; set; }
    public string? Doi { get; set; }
    public string? Url { get; set; }
    public string? PdfUrl { get; set; }
    public string? Year { get; set; }

    public string? Authors { get; set; } // comma-separated for simplicity

    public LiteratureSource Source { get; set; }
    public string? ExternalId { get; set; } // source-specific identifier

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

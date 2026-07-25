namespace SalesforceQuoteIntegration.Models.Api;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public class QuoteChangeSummary
{
    public int Id { get; set; }
    public string SalesforceQuoteId { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public List<string> ChangedFields { get; set; } = new();
    public string? QuoteName { get; set; }
    public string? Status { get; set; }
    public decimal? TotalPrice { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public long ReplayId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessingError { get; set; }
}

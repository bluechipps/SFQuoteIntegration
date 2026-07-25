namespace SalesforceQuoteIntegration.Models;

public class ApplicationLogs
{
    public int Id { get; set; }
    public string Level { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

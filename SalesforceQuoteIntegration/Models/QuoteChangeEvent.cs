namespace SalesforceQuoteIntegration.Models;

// Shared payload model used for all CDC entity types
public class SalesforceChangeEventData
{
    public SalesforcePayload? Payload { get; set; }
    public SalesforceEventMeta? Event { get; set; }
}

public class SalesforcePayload
{
    public ChangeEventHeader? ChangeEventHeader { get; set; }

    // Quote fields
    public string? Name { get; set; }
    public string? Status { get; set; }
    public decimal? TotalPrice { get; set; }
    public DateTime? ExpirationDate { get; set; }

    // Opportunity fields
    public string? StageName { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? CloseDate { get; set; }
    public string? AccountId { get; set; }

    // QuoteLineItem / OpportunityLineItem fields
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Product2Id { get; set; }
    public string? QuoteId { get; set; }
    public string? OpportunityId { get; set; }
}

public class ChangeEventHeader
{
    public string EntityName { get; set; } = string.Empty;
    public List<string> RecordIds { get; set; } = new();
    public string ChangeType { get; set; } = string.Empty;
    public List<string>? ChangedFields { get; set; }
    public long CommitTimestamp { get; set; }
}

public class SalesforceEventMeta
{
    public long ReplayId { get; set; }
}

// Keep legacy aliases so existing code compiles unchanged
public class QuoteChangeEventData : SalesforceChangeEventData { }
public class QuotePayload : SalesforcePayload { }
public class QuoteEventMeta : SalesforceEventMeta { }

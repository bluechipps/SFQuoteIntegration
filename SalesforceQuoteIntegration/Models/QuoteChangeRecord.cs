namespace SalesforceQuoteIntegration.Models;

public class ChangeEventRecord
{
    public int Id { get; set; }

    // Which Salesforce object this event relates to
    public string EntityType { get; set; } = string.Empty;      // Quote, Opportunity, QuoteLineItem, OpportunityLineItem
    public string SalesforceRecordId { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;      // CREATED, UPDATED, DELETED, UNDELETED
    public string? ChangedFields { get; set; }

    // Common fields
    public string? Name { get; set; }
    public string? Status { get; set; }
    public decimal? TotalPrice { get; set; }
    public DateTime? ExpirationDate { get; set; }

    // Opportunity fields
    public string? StageName { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? CloseDate { get; set; }
    public string? AccountId { get; set; }

    // Line item fields
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Product2Id { get; set; }
    public string? ParentId { get; set; }       // QuoteId or OpportunityId depending on entity

    // Metadata
    public string? RawPayload { get; set; }
    public string? Payload { get; set; }
    public long ReplayId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public bool IsProcessed { get; set; } = false;
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessingError { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
}

public class sfQuote
{
    public int sfQuote_id { get; set; }

    public required string Id { get; set; }
    public required string OwnerId { get; set; }
    public bool IsDeleted { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public required string OpportunityId { get; set; }
    public string? Pricebook2Id { get; set; }
    public string? ContactId { get; set; }
    public required string QuoteNumber { get; set; }
    public bool IsSyncing { get; set; }
    public decimal? ShippingHandling { get; set; }
    public decimal? Tax { get; set; }
    public string? Status { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? Description { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TotalPrice { get; set; }
    public int? LineItemCount { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
    public decimal? BillingLatitude { get; set; }
    public decimal? BillingLongitude { get; set; }
    public string? BillingGeocodeAccuracy { get; set; }
    public string? ShippingStreet { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPostalCode { get; set; }
    public string? ShippingCountry { get; set; }
    public decimal? ShippingLatitude { get; set; }
    public decimal? ShippingLongitude { get; set; }
    public string? ShippingGeocodeAccuracy { get; set; }
    public string? QuoteToStreet { get; set; }
    public string? QuoteToCity { get; set; }
    public string? QuoteToState { get; set; }
    public string? QuoteToPostalCode { get; set; }
    public string? QuoteToCountry { get; set; }
    public decimal? QuoteToLatitude { get; set; }
    public decimal? QuoteToLongitude { get; set; }
    public string? QuoteToGeocodeAccuracy { get; set; }
    public string? AdditionalStreet { get; set; }
    public string? AdditionalCity { get; set; }
    public string? AdditionalState { get; set; }
    public string? AdditionalPostalCode { get; set; }
    public string? AdditionalCountry { get; set; }
    public decimal? AdditionalLatitude { get; set; }
    public decimal? AdditionalLongitude { get; set; }
    public string? AdditionalGeocodeAccuracy { get; set; }
    public string? BillingName { get; set; }
    public string? ShippingName { get; set; }
    public string? QuoteToName { get; set; }
    public string? AdditionalName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? ContractId { get; set; }
    public string? AccountId { get; set; }
    public decimal? Discount { get; set; }
    public decimal? GrandTotal { get; set; }
    public bool CanCreateQuoteLineItems { get; set; }
    public DateTime? Quote_Date__c { get; set; }
    public DateTime? Date__c { get; set; }
    public string? EBS_Customer_ID__c { get; set; }
    public DateTime? Delivery_Date__c { get; set; }
    public string? Term_Length_In_Days__c { get; set; }
    public string? Rep_Number__c { get; set; }
    public required string Branch__c { get; set; }
    public string? Invoice__c { get; set; }
}
public class sfQuoteLineItem
{
    public int sfQuoteLineItem_id { get; set; }

    public required string Id { get; set; }
    public bool IsDeleted { get; set; }
    public required string LineNumber { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public required string QuoteId { get; set; }
    public required string PricebookEntryId { get; set; }
    public string? OpportunityLineItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Discount { get; set; }
    public string? Description { get; set; }
    public DateTime? ServiceDate { get; set; }
    public required string Product2Id { get; set; }
    public int? SortOrder { get; set; }
    public decimal? ListPrice { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? TotalPrice { get; set; }
    public string? ProductID__c { get; set; }
    public string? SerialNumber__c { get; set; }
    public string? UID__c { get; set; }
    public string? Inventory__c { get; set; }
    public string? ReservedEquipIds { get; set; }
}
public class sfOpportunity
{
    public sfOpportunity()
    {
        // Set default values for non-nullable fields
        Id = string.Empty;
        Name = string.Empty;
        StageName = string.Empty;
        ForecastCategory = string.Empty;
        OwnerId = string.Empty;
        CreatedById = string.Empty;
        LastModifiedById = string.Empty;
        OpportunityIndustry__c = string.Empty;
    }

    public int sfOpportunity_id { get; set; }

    public required string Id { get; set; }
    public bool IsDeleted { get; set; }
    public string? AccountId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string StageName { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Probability { get; set; }
    public DateTime CloseDate { get; set; }
    public string? Type { get; set; }
    public string? NextStep { get; set; }
    public string? LeadSource { get; set; }
    public bool IsClosed { get; set; }
    public bool IsWon { get; set; }
    public required string ForecastCategory { get; set; }
    public string? ForecastCategoryName { get; set; }
    public string? CampaignId { get; set; }
    public bool HasOpportunityLineItem { get; set; }
    public string? Pricebook2Id { get; set; }
    public required string OwnerId { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? AgeInDays { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public DateTime? LastActivityDate { get; set; }
    public int? LastActivityInDays { get; set; }
    public int? PushCount { get; set; }
    public DateTime? LastStageChangeDate { get; set; }
    public int? LastStageChangeInDays { get; set; }
    public int? FiscalQuarter { get; set; }
    public int? FiscalYear { get; set; }
    public string? Fiscal { get; set; }
    public string? ContactId { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public string? SyncedQuoteId { get; set; }
    public string? ContractId { get; set; }
    public bool HasOpenActivity { get; set; }
    public bool HasOverdueTask { get; set; }
    public string? LastAmountChangedHistoryId { get; set; }
    public string? LastCloseDateChangedHistoryId { get; set; }
    public bool IsPriorityRecord { get; set; }
    public bool Budget_Confirmed__c { get; set; }
    public bool Discovery_Completed__c { get; set; }
    public bool ROI_Analysis_Completed__c { get; set; }
    public string? Loss_Reason__c { get; set; }
    public required string OpportunityIndustry__c { get; set; }
    public string? Opportunity_Industry__c { get; set; }
}
public class sfOpportunityLineItem
{
    public int sfOpportunityLineItem_id { get; set; }

    public required string Id { get; set; }
    public required string OpportunityId { get; set; }
    public int? SortOrder { get; set; }
    public string? PricebookEntryId { get; set; }
    public string? Product2Id { get; set; }
    public string? ProductCode { get; set; }
    public string? Name { get; set; }
    public decimal Quantity { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? ListPrice { get; set; }
    public DateTime? ServiceDate { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
}

public class sfProduct2
{
    public int sfProduct2_id { get; set; }

    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ProductCode { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public string? Family { get; set; }
    public string? ExternalDataSourceId { get; set; }
    public string? ExternalId { get; set; }
    public string? DisplayUrl { get; set; }
    public string? QuantityUnitOfMeasure { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public string? StockKeepingUnit { get; set; }
    public string? Condition__c { get; set; }
    public string? ProductID__c { get; set; }
    public string? Product_Group__c { get; set; }
    public string? Product_Code__c { get; set; }
    public string? Revenue_Type__c { get; set; }
}

public class sfProduct2custom
{
    public int sfProduct2_id { get; set; }

    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ProductCode { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public string? Family { get; set; }
    public string? ExternalDataSourceId { get; set; }
    public string? ExternalId { get; set; }
    public string? DisplayUrl { get; set; }
    public string? QuantityUnitOfMeasure { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public string? StockKeepingUnit { get; set; }
    public string? Condition__c { get; set; }
    public string? ProductID__c { get; set; }
    public string? Product_Group__c { get; set; }
    public string? Product_Code__c { get; set; }
    public string? Revenue_Type__c { get; set; }
    public string? QuoteLineItemId { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
}

public class sfAccount
{
    public int sfAccount_id { get; set; }

    public required string Id { get; set; }
    public bool IsDeleted { get; set; }
    public string? MasterRecordId { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public string? ParentId { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountry { get; set; }
    public decimal? BillingLatitude { get; set; }
    public decimal? BillingLongitude { get; set; }
    public string? BillingGeocodeAccuracy { get; set; }
    public string? ShippingStreet { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPostalCode { get; set; }
    public string? ShippingCountry { get; set; }
    public decimal? ShippingLatitude { get; set; }
    public decimal? ShippingLongitude { get; set; }
    public string? ShippingGeocodeAccuracy { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Industry { get; set; }
    public int? NumberOfEmployees { get; set; }
    public string? Description { get; set; }
    public required string OwnerId { get; set; }
    public DateTime CreatedDate { get; set; }
    public required string CreatedById { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public required string LastModifiedById { get; set; }
    public DateTime SystemModstamp { get; set; }
    public DateTime? LastActivityDate { get; set; }
    public DateTime? LastViewedDate { get; set; }
    public DateTime? LastReferencedDate { get; set; }
    public string? Jigsaw { get; set; }
    public string? JigsawCompanyId { get; set; }
    public string? AccountSource { get; set; }
    public string? SicDesc { get; set; }
    public decimal? Account_Number__c { get; set; }
    public string? EBS_Customer_ID__c { get; set; }
    public decimal? Term_Length__c { get; set; }
    public DateTime? LastModifiedDate__c { get; set; }
}
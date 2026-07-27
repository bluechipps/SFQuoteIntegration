using Microsoft.EntityFrameworkCore;
using SalesforceQuoteIntegration.Models;

namespace SalesforceQuoteIntegration.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ChangeEventRecord> ChangeEvents { get; set; }
    public DbSet<ApplicationLogs> ApplicationLogs { get; set; }
    public DbSet<sfQuote> sfQuote { get; set; }
    public DbSet<sfQuoteLineItem> sfQuoteLineItem { get; set; }
    public DbSet<sfOpportunity> sfOpportunity { get; set; }
    public DbSet<sfOpportunityLineItem> sfOpportunityLineItem { get; set; }
    public DbSet<sfProduct2> sfProduct2 { get; set; }
    public DbSet<sfAccount> sfAccount { get; set; }
    public DbSet<sfProcessingNotification> sfProcessingNotification { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChangeEventRecord>(entity =>
        {
            entity.ToTable("ChangeEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.SalesforceRecordId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.ChangeType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ChangedFields).HasMaxLength(1000);
            entity.Property(e => e.Name).HasMaxLength(255);
            entity.Property(e => e.Status).HasMaxLength(100);
            entity.Property(e => e.StageName).HasMaxLength(100);
            entity.Property(e => e.AccountId).HasMaxLength(18);
            entity.Property(e => e.Product2Id).HasMaxLength(18);
            entity.Property(e => e.ParentId).HasMaxLength(18);
            entity.Property(e => e.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Quantity).HasColumnType("decimal(18,4)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.RawPayload).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Payload).HasColumnType("nvarchar(max)");
            entity.Property(e => e.ProcessingError).HasColumnType("nvarchar(max)");

            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.SalesforceRecordId);
            entity.HasIndex(e => e.ChangeType);
            entity.HasIndex(e => e.ReceivedAt);
            entity.HasIndex(e => e.IsProcessed);
            entity.HasIndex(e => new { e.EntityType, e.SalesforceRecordId });
        });

        modelBuilder.Entity<ApplicationLogs>(entity =>
        {
            entity.ToTable("ApplicationLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Level).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Message).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Exception).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Properties).HasColumnType("nvarchar(max)");

            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<sfQuote>(entity =>
        {
            entity.ToTable("sfQuote");
            entity.HasKey(e => e.sfQuote_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.OwnerId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
            entity.Property(e => e.OpportunityId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.Pricebook2Id).HasMaxLength(18);
            entity.Property(e => e.ContactId).HasMaxLength(18);
            entity.Property(e => e.QuoteNumber).IsRequired().HasMaxLength(30);
            entity.Property(e => e.IsSyncing).IsRequired();
            entity.Property(e => e.ShippingHandling).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Tax).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Status).HasMaxLength(40);
            entity.Property(e => e.ExpirationDate);
            entity.Property(e => e.Description).HasMaxLength(32000);
            entity.Property(e => e.Subtotal).HasColumnType("decimal(18,2)");
            entity.Property(e => e.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.LineItemCount);
            entity.Property(e => e.BillingStreet).HasMaxLength(255);
            entity.Property(e => e.BillingCity).HasMaxLength(40);
            entity.Property(e => e.BillingState).HasMaxLength(80);
            entity.Property(e => e.BillingPostalCode).HasMaxLength(20);
            entity.Property(e => e.BillingCountry).HasMaxLength(80);
            entity.Property(e => e.BillingLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.BillingLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.BillingGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.ShippingStreet).HasMaxLength(255);
            entity.Property(e => e.ShippingCity).HasMaxLength(40);
            entity.Property(e => e.ShippingState).HasMaxLength(80);
            entity.Property(e => e.ShippingPostalCode).HasMaxLength(20);
            entity.Property(e => e.ShippingCountry).HasMaxLength(80);
            entity.Property(e => e.ShippingLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.ShippingLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.ShippingGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.QuoteToStreet).HasMaxLength(255);
            entity.Property(e => e.QuoteToCity).HasMaxLength(40);
            entity.Property(e => e.QuoteToState).HasMaxLength(80);
            entity.Property(e => e.QuoteToPostalCode).HasMaxLength(20);
            entity.Property(e => e.QuoteToCountry).HasMaxLength(80);
            entity.Property(e => e.QuoteToLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.QuoteToLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.QuoteToGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.AdditionalStreet).HasMaxLength(255);
            entity.Property(e => e.AdditionalCity).HasMaxLength(40);
            entity.Property(e => e.AdditionalState).HasMaxLength(80);
            entity.Property(e => e.AdditionalPostalCode).HasMaxLength(20);
            entity.Property(e => e.AdditionalCountry).HasMaxLength(80);
            entity.Property(e => e.AdditionalLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.AdditionalLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.AdditionalGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.BillingName).HasMaxLength(255);
            entity.Property(e => e.ShippingName).HasMaxLength(255);
            entity.Property(e => e.QuoteToName).HasMaxLength(255);
            entity.Property(e => e.AdditionalName).HasMaxLength(255);
            entity.Property(e => e.Email).HasMaxLength(80);
            entity.Property(e => e.Phone).HasMaxLength(40);
            entity.Property(e => e.Fax).HasMaxLength(40);
            entity.Property(e => e.ContractId).HasMaxLength(18);
            entity.Property(e => e.AccountId).HasMaxLength(18);
            entity.Property(e => e.Discount).HasColumnType("decimal(5,2)");
            entity.Property(e => e.GrandTotal).HasColumnType("decimal(18,2)");
            entity.Property(e => e.CanCreateQuoteLineItems).IsRequired();
            entity.Property(e => e.Quote_Date__c);
            entity.Property(e => e.Date__c);
            entity.Property(e => e.EBS_Customer_ID__c).HasMaxLength(50);
            entity.Property(e => e.Delivery_Date__c);
            entity.Property(e => e.Term_Length_In_Days__c).HasMaxLength(10);
            entity.Property(e => e.Rep_Number__c).HasMaxLength(244);
            entity.Property(e => e.Branch__c).IsRequired().HasMaxLength(244);
            entity.Property(e => e.Invoice__c).HasMaxLength(18);
        });
        modelBuilder.Entity<sfQuoteLineItem>(entity =>
        {
            entity.ToTable("sfQuoteLineItem");
            entity.HasKey(e => e.sfQuoteLineItem_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.LineNumber).IsRequired().HasMaxLength(255);
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
            entity.Property(e => e.QuoteId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.PricebookEntryId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.OpportunityLineItemId).HasMaxLength(18);
            entity.Property(e => e.Quantity).IsRequired().HasColumnType("decimal(12,2)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Discount).HasColumnType("decimal(5,2)");
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.ServiceDate);
            entity.Property(e => e.Product2Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SortOrder);
            entity.Property(e => e.ListPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Subtotal).HasColumnType("decimal(18,2)");
            entity.Property(e => e.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ProductID__c).HasMaxLength(244);
            entity.Property(e => e.SerialNumber__c).HasMaxLength(45);
            entity.Property(e => e.UID__c).HasMaxLength(255);
            entity.Property(e => e.Inventory__c).HasMaxLength(18);
        });
        modelBuilder.Entity<sfOpportunity>(entity =>
        {
            entity.ToTable("sfOpportunity");
            entity.HasKey(e => e.sfOpportunity_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.AccountId).HasMaxLength(18);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
            entity.Property(e => e.Description).HasMaxLength(32000);
            entity.Property(e => e.StageName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Probability).HasColumnType("decimal(3,0)");
            entity.Property(e => e.CloseDate).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(255);
            entity.Property(e => e.NextStep).HasMaxLength(255);
            entity.Property(e => e.LeadSource).HasMaxLength(255);
            entity.Property(e => e.IsClosed).IsRequired();
            entity.Property(e => e.IsWon).IsRequired();
            entity.Property(e => e.ForecastCategory).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ForecastCategoryName).HasMaxLength(255);
            entity.Property(e => e.CampaignId).HasMaxLength(18);
            entity.Property(e => e.HasOpportunityLineItem).IsRequired();
            entity.Property(e => e.Pricebook2Id).HasMaxLength(18);
            entity.Property(e => e.OwnerId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.AgeInDays);
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.LastActivityDate);
            entity.Property(e => e.LastActivityInDays);
            entity.Property(e => e.PushCount);
            entity.Property(e => e.LastStageChangeDate);
            entity.Property(e => e.LastStageChangeInDays);
            entity.Property(e => e.FiscalQuarter);
            entity.Property(e => e.FiscalYear);
            entity.Property(e => e.Fiscal).HasMaxLength(6);
            entity.Property(e => e.ContactId).HasMaxLength(18);
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
            entity.Property(e => e.SyncedQuoteId).HasMaxLength(18);
            entity.Property(e => e.ContractId).HasMaxLength(18);
            entity.Property(e => e.HasOpenActivity).IsRequired();
            entity.Property(e => e.HasOverdueTask).IsRequired();
            entity.Property(e => e.LastAmountChangedHistoryId).HasMaxLength(18);
            entity.Property(e => e.LastCloseDateChangedHistoryId).HasMaxLength(18);
            entity.Property(e => e.IsPriorityRecord).IsRequired();
            entity.Property(e => e.Budget_Confirmed__c).IsRequired();
            entity.Property(e => e.Discovery_Completed__c).IsRequired();
            entity.Property(e => e.ROI_Analysis_Completed__c).IsRequired();
            entity.Property(e => e.Loss_Reason__c).HasMaxLength(255);
            entity.Property(e => e.OpportunityIndustry__c).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Opportunity_Industry__c).HasMaxLength(255);
        });
        modelBuilder.Entity<sfOpportunityLineItem>(entity =>
        {
            entity.ToTable("sfOpportunityLineItem");
            entity.HasKey(e => e.sfOpportunityLineItem_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.OpportunityId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SortOrder);
            entity.Property(e => e.PricebookEntryId).HasMaxLength(18);
            entity.Property(e => e.Product2Id).HasMaxLength(18);
            entity.Property(e => e.ProductCode).HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(376);
            entity.Property(e => e.Quantity).IsRequired().HasColumnType("decimal(12,2)");
            entity.Property(e => e.TotalPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ListPrice).HasColumnType("decimal(18,2)");
            entity.Property(e => e.ServiceDate);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
        });

        modelBuilder.Entity<sfProduct2>(entity =>
        {
            entity.ToTable("sfProduct2");
            entity.HasKey(e => e.sfProduct2_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ProductCode).HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.Family).HasMaxLength(255);
            entity.Property(e => e.ExternalDataSourceId).HasMaxLength(18);
            entity.Property(e => e.ExternalId).HasMaxLength(255);
            entity.Property(e => e.DisplayUrl).HasMaxLength(1000);
            entity.Property(e => e.QuantityUnitOfMeasure).HasMaxLength(255);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.IsArchived).IsRequired();
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
            entity.Property(e => e.StockKeepingUnit).HasMaxLength(180);
            entity.Property(e => e.Condition__c).HasMaxLength(255);
            entity.Property(e => e.ProductID__c).HasMaxLength(244);
            entity.Property(e => e.Product_Group__c).HasMaxLength(3);
            entity.Property(e => e.Product_Code__c).HasMaxLength(5);
            entity.Property(e => e.Revenue_Type__c).HasMaxLength(255);
        });

        modelBuilder.Entity<sfAccount>(entity =>
        {
            entity.ToTable("sfAccount");
            entity.HasKey(e => e.sfAccount_id);

            entity.Property(e => e.Id).IsRequired().HasMaxLength(18);
            entity.Property(e => e.IsDeleted).IsRequired();
            entity.Property(e => e.MasterRecordId).HasMaxLength(18);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Type).HasMaxLength(255);
            entity.Property(e => e.ParentId).HasMaxLength(18);
            entity.Property(e => e.BillingStreet).HasMaxLength(255);
            entity.Property(e => e.BillingCity).HasMaxLength(40);
            entity.Property(e => e.BillingState).HasMaxLength(80);
            entity.Property(e => e.BillingPostalCode).HasMaxLength(20);
            entity.Property(e => e.BillingCountry).HasMaxLength(80);
            entity.Property(e => e.BillingLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.BillingLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.BillingGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.ShippingStreet).HasMaxLength(255);
            entity.Property(e => e.ShippingCity).HasMaxLength(40);
            entity.Property(e => e.ShippingState).HasMaxLength(80);
            entity.Property(e => e.ShippingPostalCode).HasMaxLength(20);
            entity.Property(e => e.ShippingCountry).HasMaxLength(80);
            entity.Property(e => e.ShippingLatitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.ShippingLongitude).HasColumnType("decimal(18,15)");
            entity.Property(e => e.ShippingGeocodeAccuracy).HasMaxLength(40);
            entity.Property(e => e.Phone).HasMaxLength(40);
            entity.Property(e => e.Website).HasMaxLength(255);
            entity.Property(e => e.PhotoUrl).HasMaxLength(255);
            entity.Property(e => e.Industry).HasMaxLength(255);
            entity.Property(e => e.NumberOfEmployees);
            entity.Property(e => e.Description).HasMaxLength(32000);
            entity.Property(e => e.OwnerId).IsRequired().HasMaxLength(18);
            entity.Property(e => e.CreatedDate).IsRequired();
            entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.LastModifiedDate).IsRequired();
            entity.Property(e => e.LastModifiedById).IsRequired().HasMaxLength(18);
            entity.Property(e => e.SystemModstamp).IsRequired();
            entity.Property(e => e.LastActivityDate);
            entity.Property(e => e.LastViewedDate);
            entity.Property(e => e.LastReferencedDate);
            entity.Property(e => e.Jigsaw).HasMaxLength(20);
            entity.Property(e => e.JigsawCompanyId).HasMaxLength(20);
            entity.Property(e => e.AccountSource).HasMaxLength(255);
            entity.Property(e => e.SicDesc).HasMaxLength(80);
            entity.Property(e => e.Account_Number__c).HasColumnType("decimal(18,0)");
            entity.Property(e => e.EBS_Customer_ID__c).HasMaxLength(50);
            entity.Property(e => e.Term_Length__c).HasColumnType("decimal(18,0)");
            entity.Property(e => e.LastModifiedDate__c);
        });

        modelBuilder.Entity<sfProcessingNotification>(entity =>
        {
            entity.ToTable("sfProcessingNotifications");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RecordId).HasMaxLength(50);
            entity.Property(e => e.Title).HasMaxLength(100);
            entity.Property(e => e.Body).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Payload).HasColumnType("nvarchar(max)");
            entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.IsProcessed).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.ProcessedAt);

            entity.HasIndex(e => new { e.IsProcessed, e.CreatedAt })
                  .HasDatabaseName("IX_sfProcessingNotifications_IsProcessed");
        });
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SalesforceQuoteIntegration.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SalesforceRecordId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangedFields = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StageName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CloseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Product2Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ParentId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplayId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sfOpportunity",
                columns: table => new
                {
                    sfOpportunity_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: true),
                    StageName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Probability = table.Column<decimal>(type: "decimal(3,0)", nullable: true),
                    CloseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    NextStep = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    LeadSource = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    IsWon = table.Column<bool>(type: "bit", nullable: false),
                    ForecastCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ForecastCategoryName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CampaignId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    HasOpportunityLineItem = table.Column<bool>(type: "bit", nullable: false),
                    Pricebook2Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AgeInDays = table.Column<int>(type: "int", nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SystemModstamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastActivityInDays = table.Column<int>(type: "int", nullable: true),
                    PushCount = table.Column<int>(type: "int", nullable: true),
                    LastStageChangeDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStageChangeInDays = table.Column<int>(type: "int", nullable: true),
                    FiscalQuarter = table.Column<int>(type: "int", nullable: true),
                    FiscalYear = table.Column<int>(type: "int", nullable: true),
                    Fiscal = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    ContactId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReferencedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SyncedQuoteId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ContractId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    HasOpenActivity = table.Column<bool>(type: "bit", nullable: false),
                    HasOverdueTask = table.Column<bool>(type: "bit", nullable: false),
                    LastAmountChangedHistoryId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    LastCloseDateChangedHistoryId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    IsPriorityRecord = table.Column<bool>(type: "bit", nullable: false),
                    Budget_Confirmed__c = table.Column<bool>(type: "bit", nullable: false),
                    Discovery_Completed__c = table.Column<bool>(type: "bit", nullable: false),
                    ROI_Analysis_Completed__c = table.Column<bool>(type: "bit", nullable: false),
                    Loss_Reason__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OpportunityIndustry__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Opportunity_Industry__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sfOpportunity", x => x.sfOpportunity_id);
                });

            migrationBuilder.CreateTable(
                name: "sfOpportunityLineItem",
                columns: table => new
                {
                    sfOpportunityLineItem_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    OpportunityId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: true),
                    PricebookEntryId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Product2Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(376)", maxLength: 376, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    TotalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ServiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SystemModstamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReferencedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sfOpportunityLineItem", x => x.sfOpportunityLineItem_id);
                });

            migrationBuilder.CreateTable(
                name: "sfProduct2",
                columns: table => new
                {
                    sfProduct2_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SystemModstamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Family = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ExternalDataSourceId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DisplayUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QuantityUnitOfMeasure = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReferencedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StockKeepingUnit = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    Condition__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ProductID__c = table.Column<string>(type: "nvarchar(244)", maxLength: 244, nullable: true),
                    Product_Group__c = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Product_Code__c = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Revenue_Type__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sfProduct2", x => x.sfProduct2_id);
                });

            migrationBuilder.CreateTable(
                name: "sfQuote",
                columns: table => new
                {
                    sfQuote_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SystemModstamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReferencedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OpportunityId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    Pricebook2Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    ContactId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    QuoteNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsSyncing = table.Column<bool>(type: "bit", nullable: false),
                    ShippingHandling = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Tax = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 32000, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LineItemCount = table.Column<int>(type: "int", nullable: true),
                    BillingStreet = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    BillingCity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingState = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BillingPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    BillingCountry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BillingLatitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    BillingLongitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    BillingGeocodeAccuracy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ShippingStreet = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ShippingCity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ShippingState = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ShippingPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ShippingCountry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ShippingLatitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    ShippingLongitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    ShippingGeocodeAccuracy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuoteToStreet = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    QuoteToCity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    QuoteToState = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    QuoteToPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    QuoteToCountry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    QuoteToLatitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    QuoteToLongitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    QuoteToGeocodeAccuracy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AdditionalStreet = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AdditionalCity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AdditionalState = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AdditionalPostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AdditionalCountry = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AdditionalLatitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    AdditionalLongitude = table.Column<decimal>(type: "decimal(18,15)", nullable: true),
                    AdditionalGeocodeAccuracy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    BillingName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ShippingName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    QuoteToName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    AdditionalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Fax = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ContractId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    AccountId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Discount = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CanCreateQuoteLineItems = table.Column<bool>(type: "bit", nullable: false),
                    Quote_Date__c = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Date__c = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EBS_Customer_ID__c = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Delivery_Date__c = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Term_Length_In_Days__c = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Rep_Number__c = table.Column<string>(type: "nvarchar(244)", maxLength: 244, nullable: true),
                    Branch__c = table.Column<string>(type: "nvarchar(244)", maxLength: 244, nullable: false),
                    Invoice__c = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sfQuote", x => x.sfQuote_id);
                });

            migrationBuilder.CreateTable(
                name: "sfQuoteLineItem",
                columns: table => new
                {
                    sfQuoteLineItem_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    LineNumber = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    LastModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedById = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SystemModstamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastViewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReferencedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuoteId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    PricebookEntryId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    OpportunityLineItemId = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Discount = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ServiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Product2Id = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: true),
                    ListPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ProductID__c = table.Column<string>(type: "nvarchar(244)", maxLength: 244, nullable: true),
                    SerialNumber__c = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UID__c = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Inventory__c = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sfQuoteLineItem", x => x.sfQuoteLineItem_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLogs_Level",
                table: "ApplicationLogs",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationLogs_Timestamp",
                table: "ApplicationLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_ChangeType",
                table: "ChangeEvents",
                column: "ChangeType");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_EntityType",
                table: "ChangeEvents",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_EntityType_SalesforceRecordId",
                table: "ChangeEvents",
                columns: new[] { "EntityType", "SalesforceRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_IsProcessed",
                table: "ChangeEvents",
                column: "IsProcessed");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_ReceivedAt",
                table: "ChangeEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_SalesforceRecordId",
                table: "ChangeEvents",
                column: "SalesforceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationLogs");

            migrationBuilder.DropTable(
                name: "ChangeEvents");

            migrationBuilder.DropTable(
                name: "sfOpportunity");

            migrationBuilder.DropTable(
                name: "sfOpportunityLineItem");

            migrationBuilder.DropTable(
                name: "sfProduct2");

            migrationBuilder.DropTable(
                name: "sfQuote");

            migrationBuilder.DropTable(
                name: "sfQuoteLineItem");
        }
    }
}

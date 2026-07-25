using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SalesforceQuoteIntegration.Data;
using SalesforceQuoteIntegration.Models;
using SalesforceQuoteIntegration.Models.Api;
using SalesforceQuoteIntegration.Services;
using Serilog;

namespace SalesforceQuoteIntegration.Api;

public static class QuoteEndpoints
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Change Events");

        // GET /api/events — paginated list with filters
        group.MapGet("/", async (
            IDbContextFactory<AppDbContext> dbFactory,
            string? entityType  = null,
            string? recordId    = null,
            string? changeType  = null,
            string? status      = null,
            bool?   isProcessed = null,
            DateTime? from      = null,
            DateTime? to        = null,
            int page            = 1,
            int pageSize        = 20) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var query = db.ChangeEvents.AsQueryable();

            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(q => q.EntityType == entityType);

            if (!string.IsNullOrEmpty(recordId))
                query = query.Where(q => q.SalesforceRecordId == recordId);

            if (!string.IsNullOrEmpty(changeType))
                query = query.Where(q => q.ChangeType == changeType.ToUpper());

            if (!string.IsNullOrEmpty(status))
                query = query.Where(q => q.Status == status);

            if (isProcessed.HasValue)
                query = query.Where(q => q.IsProcessed == isProcessed.Value);

            if (from.HasValue)
                query = query.Where(q => q.ReceivedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(q => q.ReceivedAt <= to.Value);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(q => q.ReceivedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Results.Ok(new PagedResult<ChangeEventRecord>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            });
        })
        .WithName("GetChangeEvents")
        .WithSummary("Get paginated change events. Filter by entityType: Quote, QuoteLineItem, Opportunity, OpportunityLineItem");

        // GET /api/events/{id}
        group.MapGet("/{id:int}", async (
            int id,
            IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var record = await db.ChangeEvents.FindAsync(id);

            return record is null
                ? Results.NotFound(new { message = $"Change event record {id} not found" })
                : Results.Ok(record);
        })
        .WithName("GetChangeEventById")
        .WithSummary("Get a single change event record including raw payload");

        // GET /api/events/record/{recordId} — full history for any Salesforce record
        group.MapGet("/record/{recordId}", async (
            string recordId,
            IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var history = await db.ChangeEvents
                .Where(q => q.SalesforceRecordId == recordId)
                .OrderByDescending(q => q.ReceivedAt)
                .ToListAsync();

            return history.Count == 0
                ? Results.NotFound(new { message = $"No change history found for record {recordId}" })
                : Results.Ok(history);
        })
        .WithName("GetRecordHistory")
        .WithSummary("Get full change history for any Salesforce record ID");

        // GET /api/events/unprocessed
        group.MapGet("/unprocessed", async (
            IDbContextFactory<AppDbContext> dbFactory,
            string? entityType = null) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var query = db.ChangeEvents
                .Where(q => !q.IsProcessed && q.ProcessingError == null);

            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(q => q.EntityType == entityType);

            var records = await query.OrderBy(q => q.ReceivedAt).ToListAsync();
            return Results.Ok(new { Count = records.Count, Items = records });
        })
        .WithName("GetUnprocessedEvents")
        .WithSummary("Get all unprocessed change event records, optionally filtered by entityType");

        // PATCH /api/events/{id}/processed
        group.MapPatch("/{id:int}/processed", async (
            int id,
            IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var record = await db.ChangeEvents.FindAsync(id);
            if (record is null)
                return Results.NotFound(new { message = $"Record {id} not found" });

            record.IsProcessed = true;
            record.ProcessedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { message = $"Record {id} marked as processed" });
        })
        .WithName("MarkEventProcessed")
        .WithSummary("Mark a change event record as processed");

        // GET /api/events/stats
        group.MapGet("/stats", async (
            IDbContextFactory<AppDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var byEntity = await db.ChangeEvents
                .GroupBy(q => new { q.EntityType, q.ChangeType })
                .Select(g => new { g.Key.EntityType, g.Key.ChangeType, Count = g.Count() })
                .ToListAsync();

            var totalUnprocessed = await db.ChangeEvents.CountAsync(q => !q.IsProcessed);
            var last24hrs        = await db.ChangeEvents.CountAsync(q => q.ReceivedAt >= DateTime.UtcNow.AddHours(-24));

            return Results.Ok(new
            {
                ByEntityAndChangeType = byEntity,
                TotalUnprocessed      = totalUnprocessed,
                EventsLast24Hrs       = last24hrs
            });
        })
        .WithName("GetEventStats")
        .WithSummary("Get summary statistics for all change events broken down by entity type and change type");


        _ = group.MapGet("/loadaccounts", async (
            IDbContextFactory<AppDbContext> dbFactory,
            QuoteStorageService storageService,
            SalesforceQueryService queryService
        ) =>
        {
            List<JObject> results = [];
            try
            {
                results = await queryService.QueryAsync(
                $@"SELECT Id, IsDeleted, MasterRecordId, Name, Type, ParentId, BillingStreet, BillingCity, BillingState, BillingPostalCode, BillingCountry, BillingLatitude, BillingLongitude, BillingGeocodeAccuracy, ShippingStreet, ShippingCity, ShippingState, ShippingPostalCode, ShippingCountry, ShippingLatitude, ShippingLongitude, ShippingGeocodeAccuracy, Phone, Website, PhotoUrl, Industry, NumberOfEmployees, Description, OwnerId, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastActivityDate, LastViewedDate, LastReferencedDate, Jigsaw, JigsawCompanyId, AccountSource, SicDesc, Account_Number__c, EBS_Customer_ID__c, Term_Length__c, LastModifiedDate__c
                   FROM Account
                   ORDER BY CreatedDate ASC");

                if (results.Count == 0)
                {
                    Log.Warning("loadaccounts: Accounts not found");
                    return Results.Ok(new { message = "loadaccounts FAIL" });
                }

                List<sfAccount> items = [];
                foreach (var lineItem in results)
                {
                    sfAccount sfo;
                    try
                    {
                        sfAccount i = lineItem.ToObject<sfAccount>();
                        items.Add(i);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to deserialize sfAccount item {lineItem["Id"]}");
                    }
                }
                await storageService.SaveAccounts2(items);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"loadaccounts exception: {ex.Message}\r\n{ex.StackTrace}");
            }

            return Results.Ok(new { message = $"loadaccounts count: {results.Count}" });
        }).WithName("loadaccounts");
    }
}

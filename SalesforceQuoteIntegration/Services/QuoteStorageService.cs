using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;
using SalesforceQuoteIntegration.Data;
using SalesforceQuoteIntegration.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace SalesforceQuoteIntegration.Services;

public class QuoteStorageService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly RawSqlService _rawSql;

    public QuoteStorageService(IDbContextFactory<AppDbContext> dbContextFactory, RawSqlService rawSql)
    {
        _dbContextFactory = dbContextFactory;
        _rawSql = rawSql;
    }

    public async Task SaveChangeAsync(SalesforcePayload payload, long replayId, string json)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        foreach (var recordId in payload.ChangeEventHeader!.RecordIds)
        {
            string name = "";
            if (payload.Name == null && payload.ChangeEventHeader.EntityName == "Quote")
            {
                sfQuote? rec = await db.sfQuote.FirstOrDefaultAsync(q => q.Id == recordId);
                if (rec != null)
                {
                    name = rec.Name ?? "";
                }
            }
            var record = new ChangeEventRecord
            {
                EntityType = payload.ChangeEventHeader.EntityName,
                SalesforceRecordId = recordId,
                ChangeType = payload.ChangeEventHeader.ChangeType,
                ChangedFields = payload.ChangeEventHeader.ChangedFields != null
                                    ? string.Join(",", payload.ChangeEventHeader.ChangedFields)
                                    : null,

                // Common
                Name = payload.Name ?? name,
                Status = payload.Status,
                TotalPrice = payload.TotalPrice,
                ExpirationDate = payload.ExpirationDate,

                // Opportunity
                StageName = payload.StageName,
                Amount = payload.Amount,
                CloseDate = payload.CloseDate,
                AccountId = payload.AccountId,

                // Line items
                Quantity = payload.Quantity,
                UnitPrice = payload.UnitPrice,
                Product2Id = payload.Product2Id,
                ParentId = payload.QuoteId ?? payload.OpportunityId,

                RawPayload = json,
                Payload = JsonConvert.SerializeObject(payload),
                ReplayId = replayId,
                ReceivedAt = DateTime.UtcNow
            };

            db.ChangeEvents.Add(record);
        }

        try
        {
            await db.SaveChangesAsync();
            Log.Information($"Saved {payload.ChangeEventHeader.RecordIds.Count} {payload.ChangeEventHeader.EntityName} change record(s) for ReplayId {replayId}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save change event records to database for ReplayId {replayId}");
            throw;
        }
    }


    public async Task MarkAsProcessedAsync(int recordId, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var record = await db.ChangeEvents.FindAsync(recordId);
        if (record is null) return;

        record.IsProcessed = error == null;
        record.ProcessedAt = DateTime.UtcNow;
        record.ProcessingError = error;

        await db.SaveChangesAsync();
    }
    public async Task SaveOpp(sfOpportunity sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfOpportunity.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfOpportunity_id = rec.sfOpportunity_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfOpportunity.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }
    public async Task SaveOppItem(sfOpportunityLineItem sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfOpportunityLineItem.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfOpportunityLineItem_id = rec.sfOpportunityLineItem_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfOpportunityLineItem.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }
    public async Task SaveQuote(sfQuote sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfQuote.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfQuote_id = rec.sfQuote_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfQuote.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }
    public async Task SaveQuoteItem(sfQuoteLineItem sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfQuoteLineItem.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfQuoteLineItem_id = rec.sfQuoteLineItem_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfQuoteLineItem.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }
    public async Task SaveQuoteItems(List<sfQuoteLineItem> lstsfo, string qid2 = "")
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            string quoteid = lstsfo.FirstOrDefault()?.QuoteId ?? qid2;
            List<sfQuoteLineItem> incomingDeletedItems = [];

            var allIncomingIds = lstsfo.Select(s => s.Id).ToList();
            var deletedIncomingIds = lstsfo.Where(s => s.IsDeleted).Select(s => s.Id).ToList();
            //RYAN - Don't remove missing Ids, only ones marked as deleted. ALL ROWS now in the soql query
            //itemsToRemove = await db.sfQuoteLineItem
            //    .Where(qLI => qLI.QuoteId == quoteid && (!allIncomingIds.Contains(qLI.Id) || deletedIncomingIds.Contains(qLI.Id)))
            //    .ToListAsync();
            incomingDeletedItems = await db.sfQuoteLineItem
                .Where(qLI => qLI.QuoteId == quoteid && deletedIncomingIds.Contains(qLI.Id))
                .ToListAsync();


            foreach (var item in incomingDeletedItems)
            {
                //RYAN - First check for reserved equip ids and un-reserve them before removing the line item record
                if (item.ReservedEquipIds != null && item.ReservedEquipIds.Length > 0)
                {
                    item.ReservedEquipIds.Split(',').ToList().ForEach(async eqid =>
                    {
                        Log.Information($"Un-reserving {eqid} for sfQuoteLineItem {item.Id} due to line item being deleted.");
                        int rows = await _rawSql.ExecuteStoredProcedureNonQueryAsync("dbo.sp_ebs_unreserve_equip", new SqlParameter("@kequipnum", eqid), new SqlParameter("@lineId", item.Id));
                    });
                }
                //RYAN - Dont remove, just let it be marked as deleted during save changes
                //db.sfQuoteLineItem.Remove(item);
                //Log.Information($"Removed sfQuoteLineItem {item.Id} due to not found or deleted on server.");
            }

            foreach (var sfo in lstsfo)
            {
                var localQuoteItemRec = await db.sfQuoteLineItem.FirstOrDefaultAsync(q => q.Id == sfo.Id);
                if (localQuoteItemRec != null)
                {
                    sfo.sfQuoteLineItem_id = localQuoteItemRec.sfQuoteLineItem_id;

                    //RYAN - First check for reserved equip ids and un-reserve them before removing the line item record
                    if (localQuoteItemRec.ReservedEquipIds != null && localQuoteItemRec.ReservedEquipIds.Length > 0)
                    {
                        localQuoteItemRec.ReservedEquipIds.Split(',').ToList().ForEach(async eqid =>
                        {
                            Log.Information($"Un-reserving {eqid} for sfQuoteLineItem {localQuoteItemRec.Id} due to line item being deleted.");
                            string updatedReserved = await _rawSql.ExecuteStoredProcedureScalarAsync<string>(
                                "dbo.sp_ebs_sf_update_reservations",
                                new SqlParameter("@lineId", localQuoteItemRec.Id)
                            ) ?? "";
                        });
                    }


                    db.Entry(localQuoteItemRec).CurrentValues.SetValues(sfo);
                    
                }
                else
                {
                    db.sfQuoteLineItem.Add(sfo);
                }
            }

            await db.SaveChangesAsync();
            Log.Information($"Saved/Updated sfQuoteLineItem records.");


            // RYAN - Now check Quote status to see if it is already passed Approved status. If so we probably 
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"SaveQuoteItems exception: {ex.Message}\r\n{ex.StackTrace}");
            throw;
        }
    }
    public async Task SaveProduct2(sfProduct2 sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfProduct2.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfProduct2_id = rec.sfProduct2_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfProduct2.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }

    public async Task SaveAccount(sfAccount sfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var rec = await db.sfAccount.FirstOrDefaultAsync(q => q.Id == sfo.Id);
            if (rec != null)
            {
                sfo.sfAccount_id = rec.sfAccount_id;
                db.Entry(rec).CurrentValues.SetValues(sfo);
            }
            else
            {
                db.sfAccount.Add(sfo);
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved {sfo.GetType().Name} record.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to save {sfo.GetType().Name} record");
            throw;
        }
    }


    public async Task SaveAccounts(List<sfAccount> lstsfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            foreach (var sfo in lstsfo)
            {
                var rec = await db.sfAccount.FirstOrDefaultAsync(q => q.Id == sfo.Id);
                if (rec != null)
                {
                    sfo.sfAccount_id = rec.sfAccount_id;
                    db.Entry(rec).CurrentValues.SetValues(sfo);
                }
                else
                {
                    db.sfAccount.Add(sfo);
                }
            }
            await db.SaveChangesAsync();
            Log.Information($"Saved/Updated sfAccount records.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"SaveAccounts exception: {ex.Message}\r\n{ex.StackTrace}");
            throw;
        }
    }
    public async Task ResyncAccounts(List<sfAccount> lstsfo, string? error = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            await db.sfAccount.ExecuteDeleteAsync();
            db.sfAccount.AddRange(lstsfo);

            await db.SaveChangesAsync();
            Log.Information($"Saved all sfAccount records.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"ResyncAccounts exception: {ex.Message}\r\n{ex.StackTrace}");
            throw;
        }
    }

    public async Task SaveAccounts2(List<sfAccount> lstsfo, string? error = null)
    {
        if (lstsfo.Count == 0) return;

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        await using var conn = (SqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            // 1. Create a staging temp table with the same schema as sfAccount
            await db.Database.ExecuteSqlRawAsync(
                "SELECT TOP 0 * INTO #sfAccount_staging FROM sfAccount");

            // 2. Convert lstsfo into a DataTable that matches the staging schema
            var dt = ConvertToDataTable(lstsfo);

            // 3. Bulk copy the DataTable into the staging temp table
            using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = "#sfAccount_staging" })
            {
                // Map each DataTable column to the same-named staging column
                foreach (DataColumn col in dt.Columns)
                    bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

                await bulk.WriteToServerAsync(dt);
            }

            // 4. MERGE the staging table into the real table
            await db.Database.ExecuteSqlRawAsync(@"
            MERGE INTO sfAccount AS target
            USING #sfAccount_staging AS source
               ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET
                IsDeleted               = source.IsDeleted,
                MasterRecordId          = source.MasterRecordId,
                Name                    = source.Name,
                Type                    = source.Type,
                ParentId                = source.ParentId,
                BillingStreet           = source.BillingStreet,
                BillingCity             = source.BillingCity,
                BillingState            = source.BillingState,
                BillingPostalCode       = source.BillingPostalCode,
                BillingCountry          = source.BillingCountry,
                BillingLatitude         = source.BillingLatitude,
                BillingLongitude        = source.BillingLongitude,
                BillingGeocodeAccuracy  = source.BillingGeocodeAccuracy,
                ShippingStreet          = source.ShippingStreet,
                ShippingCity            = source.ShippingCity,
                ShippingState           = source.ShippingState,
                ShippingPostalCode      = source.ShippingPostalCode,
                ShippingCountry         = source.ShippingCountry,
                ShippingLatitude        = source.ShippingLatitude,
                ShippingLongitude       = source.ShippingLongitude,
                ShippingGeocodeAccuracy = source.ShippingGeocodeAccuracy,
                Phone                   = source.Phone,
                Website                 = source.Website,
                PhotoUrl                = source.PhotoUrl,
                Industry                = source.Industry,
                NumberOfEmployees       = source.NumberOfEmployees,
                Description             = source.Description,
                OwnerId                 = source.OwnerId,
                CreatedDate             = source.CreatedDate,
                CreatedById             = source.CreatedById,
                LastModifiedDate        = source.LastModifiedDate,
                LastModifiedById        = source.LastModifiedById,
                SystemModstamp          = source.SystemModstamp,
                LastActivityDate        = source.LastActivityDate,
                LastViewedDate          = source.LastViewedDate,
                LastReferencedDate      = source.LastReferencedDate,
                Jigsaw                  = source.Jigsaw,
                JigsawCompanyId         = source.JigsawCompanyId,
                AccountSource           = source.AccountSource,
                SicDesc                 = source.SicDesc,
                Account_Number__c       = source.Account_Number__c,
                EBS_Customer_ID__c      = source.EBS_Customer_ID__c,
                Term_Length__c          = source.Term_Length__c,
                LastModifiedDate__c     = source.LastModifiedDate__c
            WHEN NOT MATCHED THEN
                INSERT (
                    Id, IsDeleted, MasterRecordId, Name, Type, ParentId,
                    BillingStreet, BillingCity, BillingState, BillingPostalCode, BillingCountry,
                    BillingLatitude, BillingLongitude, BillingGeocodeAccuracy,
                    ShippingStreet, ShippingCity, ShippingState, ShippingPostalCode, ShippingCountry,
                    ShippingLatitude, ShippingLongitude, ShippingGeocodeAccuracy,
                    Phone, Website, PhotoUrl, Industry, NumberOfEmployees, Description,
                    OwnerId, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp,
                    LastActivityDate, LastViewedDate, LastReferencedDate,
                    Jigsaw, JigsawCompanyId, AccountSource, SicDesc,
                    Account_Number__c, EBS_Customer_ID__c, Term_Length__c, LastModifiedDate__c
                )
                VALUES (
                    source.Id, source.IsDeleted, source.MasterRecordId, source.Name, source.Type, source.ParentId,
                    source.BillingStreet, source.BillingCity, source.BillingState, source.BillingPostalCode, source.BillingCountry,
                    source.BillingLatitude, source.BillingLongitude, source.BillingGeocodeAccuracy,
                    source.ShippingStreet, source.ShippingCity, source.ShippingState, source.ShippingPostalCode, source.ShippingCountry,
                    source.ShippingLatitude, source.ShippingLongitude, source.ShippingGeocodeAccuracy,
                    source.Phone, source.Website, source.PhotoUrl, source.Industry, source.NumberOfEmployees, source.Description,
                    source.OwnerId, source.CreatedDate, source.CreatedById, source.LastModifiedDate, source.LastModifiedById, source.SystemModstamp,
                    source.LastActivityDate, source.LastViewedDate, source.LastReferencedDate,
                    source.Jigsaw, source.JigsawCompanyId, source.AccountSource, source.SicDesc,
                    source.Account_Number__c, source.EBS_Customer_ID__c, source.Term_Length__c, source.LastModifiedDate__c
                );

            DROP TABLE #sfAccount_staging;
        ");

            Log.Information($"Saved/Updated {lstsfo.Count} sfAccount records via MERGE");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"SaveAccounts exception: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Converts a List&lt;sfAccount&gt; into a DataTable ready for SqlBulkCopy.
    /// Column names and types match the sfAccount SQL table.
    /// The sfAccount_id column is excluded because it's an IDENTITY column
    /// and gets auto-generated on insert.
    /// </summary>
    private static DataTable ConvertToDataTable(List<sfAccount> list)
    {
        var dt = new DataTable();

        // Add columns with nullable types matching the SQL schema
        dt.Columns.Add("Id", typeof(string));
        dt.Columns.Add("IsDeleted", typeof(bool));
        dt.Columns.Add("MasterRecordId", typeof(string));
        dt.Columns.Add("Name", typeof(string));
        dt.Columns.Add("Type", typeof(string));
        dt.Columns.Add("ParentId", typeof(string));
        dt.Columns.Add("BillingStreet", typeof(string));
        dt.Columns.Add("BillingCity", typeof(string));
        dt.Columns.Add("BillingState", typeof(string));
        dt.Columns.Add("BillingPostalCode", typeof(string));
        dt.Columns.Add("BillingCountry", typeof(string));
        dt.Columns.Add("BillingLatitude", typeof(decimal));
        dt.Columns.Add("BillingLongitude", typeof(decimal));
        dt.Columns.Add("BillingGeocodeAccuracy", typeof(string));
        dt.Columns.Add("ShippingStreet", typeof(string));
        dt.Columns.Add("ShippingCity", typeof(string));
        dt.Columns.Add("ShippingState", typeof(string));
        dt.Columns.Add("ShippingPostalCode", typeof(string));
        dt.Columns.Add("ShippingCountry", typeof(string));
        dt.Columns.Add("ShippingLatitude", typeof(decimal));
        dt.Columns.Add("ShippingLongitude", typeof(decimal));
        dt.Columns.Add("ShippingGeocodeAccuracy", typeof(string));
        dt.Columns.Add("Phone", typeof(string));
        dt.Columns.Add("Website", typeof(string));
        dt.Columns.Add("PhotoUrl", typeof(string));
        dt.Columns.Add("Industry", typeof(string));
        dt.Columns.Add("NumberOfEmployees", typeof(int));
        dt.Columns.Add("Description", typeof(string));
        dt.Columns.Add("OwnerId", typeof(string));
        dt.Columns.Add("CreatedDate", typeof(DateTime));
        dt.Columns.Add("CreatedById", typeof(string));
        dt.Columns.Add("LastModifiedDate", typeof(DateTime));
        dt.Columns.Add("LastModifiedById", typeof(string));
        dt.Columns.Add("SystemModstamp", typeof(DateTime));
        dt.Columns.Add("LastActivityDate", typeof(DateTime));
        dt.Columns.Add("LastViewedDate", typeof(DateTime));
        dt.Columns.Add("LastReferencedDate", typeof(DateTime));
        dt.Columns.Add("Jigsaw", typeof(string));
        dt.Columns.Add("JigsawCompanyId", typeof(string));
        dt.Columns.Add("AccountSource", typeof(string));
        dt.Columns.Add("SicDesc", typeof(string));
        dt.Columns.Add("Account_Number__c", typeof(decimal));
        dt.Columns.Add("EBS_Customer_ID__c", typeof(string));
        dt.Columns.Add("Term_Length__c", typeof(decimal));
        dt.Columns.Add("LastModifiedDate__c", typeof(DateTime));

        // Add one row per sfAccount object — this is where lstsfo actually gets used
        foreach (var a in list)
        {
            dt.Rows.Add(
                a.Id,
                a.IsDeleted,
                (object?)a.MasterRecordId ?? DBNull.Value,
                a.Name,
                (object?)a.Type ?? DBNull.Value,
                (object?)a.ParentId ?? DBNull.Value,
                (object?)a.BillingStreet ?? DBNull.Value,
                (object?)a.BillingCity ?? DBNull.Value,
                (object?)a.BillingState ?? DBNull.Value,
                (object?)a.BillingPostalCode ?? DBNull.Value,
                (object?)a.BillingCountry ?? DBNull.Value,
                (object?)a.BillingLatitude ?? DBNull.Value,
                (object?)a.BillingLongitude ?? DBNull.Value,
                (object?)a.BillingGeocodeAccuracy ?? DBNull.Value,
                (object?)a.ShippingStreet ?? DBNull.Value,
                (object?)a.ShippingCity ?? DBNull.Value,
                (object?)a.ShippingState ?? DBNull.Value,
                (object?)a.ShippingPostalCode ?? DBNull.Value,
                (object?)a.ShippingCountry ?? DBNull.Value,
                (object?)a.ShippingLatitude ?? DBNull.Value,
                (object?)a.ShippingLongitude ?? DBNull.Value,
                (object?)a.ShippingGeocodeAccuracy ?? DBNull.Value,
                (object?)a.Phone ?? DBNull.Value,
                (object?)a.Website ?? DBNull.Value,
                (object?)a.PhotoUrl ?? DBNull.Value,
                (object?)a.Industry ?? DBNull.Value,
                (object?)a.NumberOfEmployees ?? DBNull.Value,
                (object?)a.Description ?? DBNull.Value,
                a.OwnerId,
                a.CreatedDate,
                a.CreatedById,
                a.LastModifiedDate,
                a.LastModifiedById,
                a.SystemModstamp,
                (object?)a.LastActivityDate ?? DBNull.Value,
                (object?)a.LastViewedDate ?? DBNull.Value,
                (object?)a.LastReferencedDate ?? DBNull.Value,
                (object?)a.Jigsaw ?? DBNull.Value,
                (object?)a.JigsawCompanyId ?? DBNull.Value,
                (object?)a.AccountSource ?? DBNull.Value,
                (object?)a.SicDesc ?? DBNull.Value,
                (object?)a.Account_Number__c ?? DBNull.Value,
                (object?)a.EBS_Customer_ID__c ?? DBNull.Value,
                (object?)a.Term_Length__c ?? DBNull.Value,
                (object?)a.LastModifiedDate__c ?? DBNull.Value
            );
        }

        return dt;
    }

    public async Task<long> GetLastReplayIdAsync(string entityType)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Replay IDs are per-channel in Salesforce, so the last replay ID for the
        // Quote channel is unrelated to the last replay ID for the Opportunity
        // channel. Filter by entity type so each channel resumes from its own
        // last-seen event rather than a global maximum that only makes sense for
        // one channel.
        var last = await db.ChangeEvents
            .Where(q => q.EntityType == entityType)
            .OrderByDescending(q => q.ReplayId)
            .FirstOrDefaultAsync();

        // -2 tells Salesforce to replay everything still in its 72-hour buffer
        // (used the first time we ever subscribe to this channel).
        return last?.ReplayId ?? -2;
    }

    public async Task<List<ChangeEventRecord>> GetUnprocessedChangesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        return await db.ChangeEvents
            .Where(q => q.EntityType == "Quote" && q.Status == "Accepted" && !q.IsProcessed && q.ProcessingError == null)
            .OrderBy(q => q.ReceivedAt)
            .ToListAsync();
    }
    public async Task<ChangeEventRecord?> GetNextUnprocessedChangeAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var oldestUnprocessed = await db.ChangeEvents
            .Where(q => q.EntityType == "Quote" && q.Status == "Accepted" && !q.IsProcessed && q.ProcessingError == null)
            .OrderBy(q => q.ReceivedAt)
            .Select(q => new
            {
                Record = q,
                HasNewer = db.ChangeEvents.Any(n =>
                    n.EntityType == "Quote" &&
                    n.SalesforceRecordId == q.SalesforceRecordId &&
                    n.ReceivedAt > q.ReceivedAt &&
                    n.ProcessingError == null)
            })
            .FirstOrDefaultAsync();

        if (oldestUnprocessed == null)
            return null;

        if (oldestUnprocessed.HasNewer)
        {
            Log.Information("Found a more recent valid change event which makes this unprocessed one obsolete.");
            await MarkAsProcessedAsync(oldestUnprocessed.Record.Id, "Obsolete due to newer change event");
            return null;
        }

        return oldestUnprocessed.Record;
    }

    public async Task<sfQuote?> GetQuoteByIdAsync(string quoteId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.sfQuote.FirstOrDefaultAsync(q => q.Id == quoteId);
    }
    public async Task<List<sfQuoteLineItem>> GetQuoteLinesByIdAsync(string quoteId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.sfQuoteLineItem.Where(q => q.QuoteId == quoteId).ToListAsync();
    }
    public async Task<sfProduct2?> GetProductByIdAsync(string productId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.sfProduct2.FirstOrDefaultAsync(q => q.Id == productId);
    }
}

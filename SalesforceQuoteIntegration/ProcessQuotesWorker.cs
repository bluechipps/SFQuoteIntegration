using SalesforceQuoteIntegration.Models;
using SalesforceQuoteIntegration.Services;
using Serilog;

public class ProcessQuotesWorker : BackgroundService
{
    private readonly RawSqlService _rawSql;
    private readonly SalesforceQueryService _queryService;
    private readonly QuoteStorageService _storageService;

    public ProcessQuotesWorker(
        RawSqlService rawSql,
        SalesforceQueryService queryService,
        QuoteStorageService storageService)
    {
        _rawSql = rawSql;
        _queryService = queryService;
        _storageService = storageService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tokenSource = new CancellationTokenSource();
        stoppingToken.Register(() => tokenSource.Cancel());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await _rawSql.ExecuteReaderAsync(@$"
SELECT TOP 50 Id, EventType, kordnum, kbranch, Payload FROM ChangeEvents WHERE IsProcessed = 0 ORDER BY CreatedAt
");

                ChangeEventRecord? chrec;
                try
                {
                    chrec = await _storageService.GetNextUnprocessedChangeAsync();
                    if (chrec != null)
                    {
                        Log.Information($"Quote {chrec.Name} ({chrec.SalesforceRecordId}) — ready to process.");
                    }
                    else
                    {
                        //Log.Information($"No unprocessed quote records found.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Critical error retrieving unprocessed Quote change event: {ex.Message}\r\n{ex.StackTrace}");
                    // cancel current processing service                    
                }

                foreach (var row in pending)
                {
                    var id = (int)row["Id"]!;
                    var eventType = row["EventType"]?.ToString();
                    int kordnum = (int)row["kordnum"]!;
                    var kbranch = row["kbranch"]?.ToString();

                    Log.Information($"Processing notification {id}: {eventType} for {kordnum}");

                    //await HandleNotificationAsync(eventType!, kordnum!, kbranch!);
                    // Example: send a Salesforce custom notification
                    //await _queryService.HandleUnprocessedQuoteAsync();

                    await _queryService.SendCustomNotificationAsync(
                        customNotifTypeId: "Order_Complete",
                        recipientIds: new List<string> { "ownerId" },
                        title: "Order Complete",
                        body: $"Kordnum {kordnum} Kbranch {kbranch} has finished processing",
                        targetId: "");

                    // Mark as processed
                    await _rawSql.ExecuteNonQueryAsync($"UPDATE ProcessingNotifications SET IsProcessed = 1, ProcessedAt = SYSUTCDATETIME() WHERE Id = {id}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error polling ProcessingNotifications");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task HandleNotificationAsync(string eventType, int kordnum, string kbranch)
    {
        
    }
}
using SalesforceQuoteIntegration.Services;
using Serilog;

public class ProcessingNotificationsWorker : BackgroundService
{
    private readonly RawSqlService _rawSql;
    private readonly SalesforceQueryService _queryService;

    public ProcessingNotificationsWorker(
        RawSqlService rawSql,
        SalesforceQueryService queryService)
    {
        _rawSql = rawSql;
        _queryService = queryService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await _rawSql.ExecuteReaderAsync(@$"
SELECT TOP 50 Id, EventType, kordnum, kbranch, Payload FROM ProcessingNotifications WHERE IsProcessed = 0 ORDER BY CreatedAt
");

                foreach (var row in pending)
                {
                    var id = (int)row["Id"]!;
                    var eventType = row["EventType"]?.ToString();
                    int kordnum = (int)row["kordnum"]!;
                    var kbranch = row["kbranch"]?.ToString();

                    Log.Information($"Processing notification {id}: {eventType} for {kordnum}");

                    await HandleNotificationAsync(eventType!, kordnum!, kbranch!);

                    // Mark as processed
                    await _rawSql.ExecuteNonQueryAsync(
                        $"UPDATE ProcessingNotifications SET IsProcessed = 1, ProcessedAt = SYSUTCDATETIME() WHERE Id = {id}");
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
        // Example: send a Salesforce custom notification
        await _queryService.SendCustomNotificationAsync(
            customNotifTypeId: "Order_Complete",
            recipientIds: new List<string> { "ownerId" },
            title: "Order Complete",
            body: $"Kordnum {kordnum} Kbranch {kbranch} has finished processing",
            targetId: "");
    }
}
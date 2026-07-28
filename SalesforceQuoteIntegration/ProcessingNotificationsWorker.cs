using SalesforceQuoteIntegration.Services;
using Serilog;

public class ProcessingNotificationsWorker : BackgroundService
{
    private readonly RawSqlService _rawSql;
    private readonly SalesforceQueryService _queryService;
    private readonly QuoteStorageService _storageService;

    public ProcessingNotificationsWorker(
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
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending = await _rawSql.ExecuteReaderAsync(@$"
SELECT TOP 50 Id, EventType, RecordId, Title, Body, Payload, CreatedAt, IsProcessed, ProcessedAt
FROM sfProcessingNotifications WHERE IsProcessed = 0 ORDER BY CreatedAt
");
                foreach (var row in pending)
                {
                    var id = (int)row["Id"]!;
                    string eventType = row["EventType"]?.ToString() ?? "";
                    string title = row["Title"]?.ToString() ?? "";
                    string body = row["Body"]?.ToString() ?? "";
                    string recordId = row["RecordId"]?.ToString() ?? "";

                    Log.Information($"Processing notification {id}: {eventType}. Body: {body}");

                    await HandleNotificationAsync(eventType!, title, body, recordId);

                    // Mark as processed
                    await _rawSql.ExecuteNonQueryAsync(
                        $"UPDATE sfProcessingNotifications SET IsProcessed = 1, ProcessedAt = SYSUTCDATETIME() WHERE Id = {id}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error polling sfProcessingNotifications");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task HandleNotificationAsync(string eventType, string title, string body, string recordId = "")
    {
        List<string> r = new List<string>();
        r = await _queryService.GetGroupMembers("QuoteNotifications");
        if (!r.Contains("005O100000SlPxKIAV")) //r.Count == 0
        {
            //r.Add("005O100000SlPxKIAV"); //asheranko
        }
        if (recordId != "")
        {
            var quote = await _storageService.GetQuoteByIdAsync(recordId);
            if (quote != null && !string.IsNullOrEmpty(quote.LastModifiedById))
            {
                if (!r.Contains(quote.LastModifiedById))
                {
                    r.Add(quote.LastModifiedById);
                }
            }
        }
        await _queryService.SendCustomNotificationAsync(
            customNotifTypeId: eventType,
            recipientIds: r,
            title: title,
            body: body,
            targetId: recordId);
    }
}
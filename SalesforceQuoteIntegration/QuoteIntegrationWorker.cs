using Microsoft.Extensions.Hosting;
using Serilog;
using SalesforceQuoteIntegration.Services;

namespace SalesforceQuoteIntegration;

public class QuoteIntegrationWorker : BackgroundService
{
    private readonly QuoteChangeEventService _quoteService;

    public QuoteIntegrationWorker(QuoteChangeEventService quoteService)
    {
        _quoteService = quoteService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _quoteService.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Quote Integration Worker stopped gracefully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Quote Integration Worker encountered a fatal error");
            throw;
        }
    }
}

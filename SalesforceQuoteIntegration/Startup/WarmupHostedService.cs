using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Serilog;

namespace SalesforceQuoteIntegration.Startup;

/// <summary>
/// Sends a self-request on startup to wake IIS and ensure the app
/// is fully initialized and subscribed before the first real request arrives.
/// </summary>
public class WarmupHostedService : IHostedService
{
    private readonly IServer _server;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _lifetime;

    public WarmupHostedService(
        IServer server,
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime lifetime)
    {
        _server = server;
        _httpClientFactory = httpClientFactory;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait until the app is fully started, then send a self-request
        _lifetime.ApplicationStarted.Register(async () =>
        {
            try
            {
                // Give the server a moment to be fully ready
                await Task.Delay(500);

                var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
                var address = addresses?.FirstOrDefault() ?? "http://localhost";

                var client = _httpClientFactory.CreateClient("warmup");
                var response = await client.GetAsync($"{address}/health");

                Log.Information($"Warmup request completed with status {response.StatusCode}");
            }
            catch (Exception ex)
            {
                // Non-fatal — the background service is already running regardless
                Log.Warning(ex, "Warmup request failed — app is still running normally");
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
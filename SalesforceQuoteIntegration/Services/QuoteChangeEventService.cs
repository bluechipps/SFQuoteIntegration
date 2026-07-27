using CometD.NetCore.Bayeux.Client;
using CometD.NetCore.Client;
using CometD.NetCore.Client.Transport;
using Newtonsoft.Json;
using Serilog;
using SalesforceQuoteIntegration.Models;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Client.Extension;

namespace SalesforceQuoteIntegration.Services;

public class QuoteChangeEventService
{
    private readonly SalesforceAuthService _authService;
    private readonly QuoteStorageService _storageService;
    private readonly SalesforceQueryService _queryService;
    private BayeuxClient? _bayeuxClient;

    private DateTime _lastConnectAt = DateTime.UtcNow;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(2);
    private const string ApiVersion = "59.0";
    private readonly HashSet<string> _channelsNeedingReplayReset = new();

    private static readonly Dictionary<string, string> Channels = new()
    {
        ["/data/QuoteChangeEvent"]              = "Quote",
        ["/data/QuoteLineItemChangeEvent"]      = "QuoteLineItem",
        //["/data/OpportunityChangeEvent"]        = "Opportunity",
        //["/data/OpportunityLineItemChangeEvent"] = "OpportunityLineItem",
        ["/data/AccountChangeEvent"] = "Account"
    };

    public QuoteChangeEventService(
        SalesforceAuthService authService,
        QuoteStorageService storageService,
        SalesforceQueryService queryService)
    {
        _authService  = authService;
        _storageService = storageService;
        _queryService = queryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CometD connection failed — reconnecting in 30 seconds");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        var (accessToken, instanceUrl) = await _authService.GetTokenAsync();

        LongPollingTransport transport = new LongPollingTransport(null,
            new System.Collections.Specialized.NameValueCollection
            {
            { "Authorization", $"Bearer {accessToken}" }
        });

        _bayeuxClient = new BayeuxClient($"{instanceUrl}/cometd/{ApiVersion}", new[] { transport });

        // Add the replay extension ONCE — it tracks replay IDs per channel automatically
        // by reading the replayId off each incoming event.
        _bayeuxClient.AddExtension(new ReplayExtension());

        _lastConnectAt = DateTime.UtcNow;
        _bayeuxClient.GetChannel("/meta/handshake").AddListener(new MetaHandshakeListener());
        _bayeuxClient.GetChannel("/meta/connect").AddListener(new MetaConnectListener(this));
        _bayeuxClient.GetChannel("/meta/subscribe").AddListener(new MetaSubscribeListener(this));
        _bayeuxClient.Handshake();

        BayeuxClient.State state = _bayeuxClient.WaitFor(15000, new[] { BayeuxClient.State.CONNECTED });
        if (state != BayeuxClient.State.CONNECTED)
        {
            Log.Fatal("Failed to connect to Salesforce Streaming API");
            throw new Exception("Failed to connect to Salesforce Streaming API");
        }
        Log.Information($"Connected to Salesforce Streaming API at {instanceUrl}");

        foreach (var (channelName, entityType) in Channels)
        {
            long lastReplayId = _channelsNeedingReplayReset.Contains(channelName)
                ? -2                                                    // stale — replay all buffered
                : await _storageService.GetLastReplayIdAsync(entityType);

            Log.Information($"Subscribed to {channelName} ({entityType}). Resuming {entityType} from ReplayId {lastReplayId}");

            var channel = _bayeuxClient.GetChannel(channelName, lastReplayId);
            channel.Subscribe(new ChangeEventListener(entityType, _storageService, _queryService));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            var timeSinceLastConnect = DateTime.UtcNow - _lastConnectAt;

            if (_lastConnectAt == DateTime.MinValue)
            {
                Log.Warning("Reconnecting due to 403::Unknown client (session invalidated)");
                break;
            }
            if (timeSinceLastConnect > ConnectTimeout)
            {
                Log.Warning($"No /meta/connect heartbeat for {timeSinceLastConnect.TotalMinutes:F1} minutes — reconnecting");
                break;
            }

            await RefreshTokenIfNeededAsync();
        }

        try
        {
            _bayeuxClient.ResetSubscriptions();
            _bayeuxClient.Disconnect();
            _bayeuxClient.WaitFor(3000, new[] { BayeuxClient.State.DISCONNECTED });
            Log.Information("Cleanly disconnected from Salesforce Streaming API");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during disconnect — ignoring");
        }
        _bayeuxClient = null;
    }

    private async Task RefreshTokenIfNeededAsync()
    {
        try
        {
            var (newToken, _) = await _authService.GetTokenAsync();
            _bayeuxClient?.SetAttribute("Authorization", $"Bearer {newToken}");
            Log.Debug($"Salesforce token refreshed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Salesforce token");
        }
    }

    private class MetaHandshakeListener : IMessageListener
    {
        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            if (message.Successful)
            {
                System.Diagnostics.Debug.Print($"CometD /meta/handshake successful at {DateTime.UtcNow}.");
                return;
            }

            var error = message.ContainsKey("error") ? message["error"]?.ToString() : "none";
            var advice = message.ContainsKey("advice") ? message["advice"]?.ToString() : "none";

            Log.Fatal($"Handshake failed | Error: {error} | Advice: {advice} | Full: {JsonConvert.SerializeObject(message)}");
        }
    }

    private class MetaSubscribeListener : IMessageListener
    {
        private readonly QuoteChangeEventService _parent;

        public MetaSubscribeListener(QuoteChangeEventService parent) => _parent = parent;

        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            if (message.Successful)
            {
                Log.Information($"Subscribe confirmed: {message["subscription"]}");
                return;
            }

            var error = message.ContainsKey("error") ? message["error"]?.ToString() ?? "" : "";
            var subscription = message["subscription"]?.ToString() ?? "";

            if (error.Contains("replayId") && error.Contains("invalid"))
            {
                Log.Warning($"Replay ID for {subscription} is stale — resubscribing from -2");
                _parent._channelsNeedingReplayReset.Add(subscription);
                _parent._lastConnectAt = DateTime.MinValue;  // trigger reconnect loop
            }
            else
            {
                Log.Error($"Subscribe failed for {subscription} | Error: {error}");
            }
        }
    }

    private class MetaConnectListener : IMessageListener
    {
        private readonly QuoteChangeEventService _parent;

        public MetaConnectListener(QuoteChangeEventService parent)
        {
            _parent = parent;
        }
        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            if (!message.Successful)
            {
                var error = message.ContainsKey("error") ? message["error"]?.ToString() : "none";
                Log.Warning($"CometD /meta/connect unsuccessful | Error: {error}");

                // Any 403 (session invalidated) should trigger a full reconnect.
                // Salesforce uses several 403 variants, so match the code, not exact text.
                if (error != null && error.Contains("403"))
                {
                    Log.Warning("CometD session invalidated — flagging for reconnect");
                    _parent._lastConnectAt = DateTime.MinValue;
                }
                // Do NOT update _lastConnectAt to UtcNow on a failed connect —
                // otherwise the health loop mistakes failed polls for healthy heartbeats.
                return;
            }

            // Only a SUCCESSFUL connect counts as a real heartbeat
            _parent._lastConnectAt = DateTime.UtcNow;
            Log.Debug($"CometD /meta/connect heartbeat at {_parent._lastConnectAt}");
        }
    }

    private class ChangeEventListener : IMessageListener
    {
        private readonly string _entityType;
        private readonly QuoteStorageService _storageService;
        private readonly SalesforceQueryService _queryService;

        public ChangeEventListener(
            string entityType,
            QuoteStorageService storageService,
            SalesforceQueryService queryService)
        {
            _entityType     = entityType;
            _storageService = storageService;
            _queryService   = queryService;
        }

        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            //Log.Information($"[{_entityType}] Raw message received: {message.Json}");
            try
            {
                var json      = JsonConvert.SerializeObject(message.Data);
                var eventData = JsonConvert.DeserializeObject<SalesforceChangeEventData>(json);
                var payload   = eventData?.Payload;
                var replayId  = eventData?.Event?.ReplayId ?? 0;

                if (payload?.ChangeEventHeader is null)
                {
                    Log.Warning($"[{_entityType}] Received message with missing ChangeEventHeader, skipping");
                    return;
                }

                Log.Information($"[{_entityType}] Received {payload.ChangeEventHeader.ChangeType} for Record(s) {string.Join(", ", payload.ChangeEventHeader.RecordIds)} | ReplayId: {replayId}");

                Task.Run(async () =>
                {
                    try
                    {
                        await _storageService.SaveChangeAsync(payload, replayId, json);

                        foreach (var recordId in payload.ChangeEventHeader.RecordIds)
                        {
                            var record = new ChangeEventRecord
                            {
                                EntityType         = _entityType,
                                SalesforceRecordId = recordId,
                                ChangeType         = payload.ChangeEventHeader.ChangeType,
                                ChangedFields = payload.ChangeEventHeader.ChangedFields != null
                                     ? string.Join(",", payload.ChangeEventHeader.ChangedFields)
                                     : null,
                                ReplayId           = replayId
                            };

                            await DispatchQueryHandlerAsync(record);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[{_entityType}] Failed to process event for ReplayId {replayId}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{_entityType}] Unhandled error processing message");
            }
        }

        private Task DispatchQueryHandlerAsync(ChangeEventRecord record) =>
            _entityType switch
            {
                "Quote"                => _queryService.OnQuoteChangedAsync(record),
                "QuoteLineItem"        => _queryService.OnQuoteLineItemChangedAsync(record),
                //"Opportunity"          => _queryService.OnOpportunityChangedAsync(record),
                //"OpportunityLineItem"  => _queryService.OnOpportunityLineItemChangedAsync(record),
                "Account"              => _queryService.OnAccountChangedAsync(record),
                _ => Task.CompletedTask
            };
    }

}

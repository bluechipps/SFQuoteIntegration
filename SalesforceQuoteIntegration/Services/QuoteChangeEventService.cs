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

        //_bayeuxClient.GetChannel("/**").AddListener(new WildcardListener());

        /*
         * OLD subscribe loop
         * 
        foreach (var (channelName, entityType) in Channels)
        {
            // Each channel resumes from its own last-seen replay ID. Replay IDs
            // are per-channel in Salesforce, so a single shared value would be
            // wrong for all but one channel.
            var lastReplayId = await _storageService.GetLastReplayIdAsync(entityType);
            Log.Information($"Resuming {entityType} from ReplayId {lastReplayId}");

            var channel = _bayeuxClient.GetChannel(channelName, lastReplayId);
            channel.Subscribe(new ChangeEventListener(entityType, _storageService, _queryService));
            Log.Information($"Subscribed to {channelName} ({entityType})");
        }
        
        foreach (var (channelName, entityType) in Channels)
        {
            var lastReplayId = await _storageService.GetLastReplayIdAsync(entityType);
            Log.Information($"Resuming {entityType} from ReplayId {lastReplayId}");

            // Register replay extension for this channel (sets the resume point)
            _bayeuxClient.GetChannel(channelName, lastReplayId);

            // Attach the listener to the PLAIN channel — this is where messages are delivered
            _bayeuxClient.GetChannel(channelName).Subscribe(new ChangeEventListener(entityType, _storageService, _queryService));

            Log.Information($"Subscribed to {channelName} ({entityType})");
        }
        

        foreach (var (channelName, entityType) in Channels)
        {
            // Subscribe on the PLAIN channel — the ReplayExtension handles resume
            // automatically. Do NOT use the GetChannel(name, replayId) overload;
            // its channel wrapper does not reliably deliver messages to listeners.
            _bayeuxClient.GetChannel(channelName)
                .Subscribe(new ChangeEventListener(entityType, _storageService, _queryService));

            Log.Information($"Subscribed to {channelName} ({entityType})");
        }
        */

        // Subscribe once to the wildcard channel — proven to receive all data messages
        _bayeuxClient.GetChannel("/**").AddListener(new WildcardChangeEventListener(_storageService, _queryService));

        // Still call Subscribe on each data channel so Salesforce sends them,
        // but rely on the wildcard listener for delivery
        //foreach (var (channelName, entityType) in Channels)
        //{
        //    var lastReplayId = await _storageService.GetLastReplayIdAsync(entityType);
        //    _bayeuxClient.GetChannel(channelName, lastReplayId).Subscribe(new NoOpListener());
        //    Log.Information($"Subscribed to {channelName} ({entityType})");
        //}
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
            if (timeSinceLastConnect > ConnectTimeout)
            {
                Log.Warning($"No /meta/connect heartbeat for {timeSinceLastConnect.TotalMinutes:F1} minutes — reconnecting");
                break;
            }
            await RefreshTokenIfNeededAsync();
            //Log.Information($"Health check OK | Last connect: {ConnectAgo}s ago", (int)timeSinceLastConnect.TotalSeconds);
        }
        //while (!cancellationToken.IsCancellationRequested)
        //{
        //    await Task.Delay(TimeSpan.FromMinutes(15), cancellationToken);
        //    await RefreshTokenIfNeededAsync();
        //}

        try
        {
            _bayeuxClient.Disconnect();
            _bayeuxClient.WaitFor(3000, new[] { BayeuxClient.State.DISCONNECTED });
            Log.Information("Cleanly disconnected from Salesforce Streaming API");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during disconnect — ignoring");
        }
    }

    private async Task RefreshTokenIfNeededAsync()
    {
        try
        {
            var (newToken, _) = await _authService.GetTokenAsync();
            _bayeuxClient?.SetAttribute("Authorization", $"Bearer {newToken}");
            //Log.Debug($"Salesforce token refreshed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh Salesforce token");
        }
    }

    private class WildcardChangeEventListener : IMessageListener
    {
        private readonly QuoteStorageService _storageService;
        private readonly SalesforceQueryService _queryService;

        private static readonly Dictionary<string, string> ChannelToEntity = new()
        {
            ["/data/QuoteChangeEvent"] = "Quote",
            ["/data/QuoteLineItemChangeEvent"] = "QuoteLineItem",
            ["/data/AccountChangeEvent"] = "Account",
        };

        public WildcardChangeEventListener(QuoteStorageService storageService, SalesforceQueryService queryService)
        {
            _storageService = storageService;
            _queryService = queryService;
        }

        public void OnMessage(IClientSessionChannel channel, IMessage message)
        {
            System.Diagnostics.Debug.Print($"[WILDCARD] Channel: {channel.Id} | Data present: {message.Data != null} | Json: {message.Json}");
            Log.Information($"[WILDCARD] Channel: {channel.Id} | Data present: {message.Data != null} | Json: {message.Json}");

            // Ignore meta messages (/meta/connect, /meta/subscribe, etc.) and anything without data
            if (message.Data == null) return;

            // The actual channel is in the message, e.g. "/data/AccountChangeEvent"
            var msgChannel = message.Channel;
            if (msgChannel == null || !ChannelToEntity.TryGetValue(msgChannel, out var entityType))
                return;

            try
            {
                var json = JsonConvert.SerializeObject(message.Data);
                var eventData = JsonConvert.DeserializeObject<SalesforceChangeEventData>(json);
                var payload = eventData?.Payload;
                var replayId = eventData?.Event?.ReplayId ?? 0;

                if (payload?.ChangeEventHeader is null)
                {
                    Log.Warning($"[{entityType}] Missing ChangeEventHeader, skipping");
                    return;
                }

                Log.Information($"[{entityType}] Received {payload.ChangeEventHeader.ChangeType} for Record(s) {string.Join(", ", payload.ChangeEventHeader.RecordIds)} | ReplayId: {replayId}");

                Task.Run(async () =>
                {
                    try
                    {
                        await _storageService.SaveChangeAsync(payload, replayId, json);
                        foreach (var recordId in payload.ChangeEventHeader.RecordIds)
                        {
                            var record = new ChangeEventRecord
                            {
                                EntityType = entityType,
                                SalesforceRecordId = recordId,
                                ChangeType = payload.ChangeEventHeader.ChangeType,
                                ReplayId = replayId
                            };
                            await DispatchQueryHandlerAsync(record);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"[{entityType}] Failed to process event for ReplayId {replayId}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[{entityType}] Unhandled error processing message");
            }
        }

        private Task DispatchQueryHandlerAsync(ChangeEventRecord record) =>
            record.EntityType switch
            {
                "Quote" => _queryService.OnQuoteChangedAsync(record),
                "QuoteLineItem" => _queryService.OnQuoteLineItemChangedAsync(record),
                "Account" => _queryService.OnAccountChangedAsync(record),
                _ => Task.CompletedTask
            };
    }

    private class NoOpListener : IMessageListener
    {
        public void OnMessage(IClientSessionChannel channel, IMessage message) { }
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

    //private class MetaSubscribeListener : IMessageListener
    //{
    //    public void OnMessage(IClientSessionChannel channel, IMessage message)
    //    {
    //        if (message.Successful)
    //        {
    //            Log.Information($"Subscribe confirmed: {message.Channel} | subscription: {message["subscription"]}");
    //        }
    //        else
    //        {
    //            var error = message.ContainsKey("error") ? message["error"]?.ToString() : "none";
    //            var advice = message.ContainsKey("advice") ? message["advice"]?.ToString() : "none";
    //            Log.Error($"Subscribe FAILED: {message["subscription"]} | Error: {error} | Advice: {advice}");
    //        }
    //    }
    //}
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
            _parent._lastConnectAt = DateTime.UtcNow;
            if (!message.Successful)
            {
                var error = message.ContainsKey("error") ? message["error"]?.ToString() : "none";
                Log.Warning($"CometD /meta/connect unsuccessful | Error: {error}");

                if (error != null && error.Contains("403::Unknown client"))
                {
                    Log.Warning("CometD session expired — flagging for reconnect");
                    _parent._lastConnectAt = DateTime.MinValue;
                }
            }
            else
            {
                Log.Debug($"CometD /meta/connect heartbeat at {_parent._lastConnectAt}");
            }

            //if (!message.Successful)
            //{
            //    var error = message.ContainsKey("error") ? message["error"]?.ToString() : "none";
            //    Log.Warning($"CometD /meta/connect unsuccessful | Error: {Error}", error);
            //}
            //else
            //{
            //    System.Diagnostics.Debug.Print($"CometD /meta/connect heartbeat at {_parent._lastConnectAt}");
            //    Log.Debug($"CometD /meta/connect heartbeat at {_parent._lastConnectAt}");
            //}
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
            Log.Information($"[{_entityType}] Raw message received: {message.Json}");
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

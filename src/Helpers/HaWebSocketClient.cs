namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class HaWebSocketClient : IAsyncDisposable, IDisposable
    {
        // ====================================================================
        // CONSTANTS - WebSocket Client Configuration
        // ====================================================================

        // --- Connection Constants ---
        private const Int32 InitialMessageId = 1;                     // Initial message ID for WebSocket communication
        private const Int32 HealthCheckTimeoutSeconds = 5;            // Timeout for health check operations
        private const Int32 ReconnectionTimeoutSeconds = 8;           // Timeout for reconnection attempts
        private const Int32 PongResponseDelayMs = 100;                // Delay to wait for pong response after ping

        // --- Buffer and Logging Constants ---
        private const Int32 WebSocketBufferSize = 8192;               // Buffer size for WebSocket receive operations
        private const Int32 LogDataTruncationLength = 200;            // Length at which to truncate data for logging

        private ClientWebSocket? _ws;
        private readonly Object _gate = new();
        private Int32 _nextId = InitialMessageId;
        public Boolean IsAuthenticated { get; private set; }
        public Uri? EndpointUri { get; private set; }

        private String? _lastBaseUrl;
        private String? _lastAccessToken;


        public async Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(
            String baseUrl, String accessToken, TimeSpan timeout, CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Debug(() => $"[WS] ConnectAndAuthenticateAsync START - baseUrl='{baseUrl}', timeout={timeout.TotalSeconds}s");

            try
            {
                if (String.IsNullOrWhiteSpace(baseUrl))
                {
                    PluginLog.Warning("[WS] Connect failed - Base URL is empty");
                    return (false, "Base URL is empty");
                }

                if (String.IsNullOrWhiteSpace(accessToken))
                {
                    PluginLog.Warning("[WS] Connect failed - Access token is empty");
                    return (false, "Access token is empty");
                }

                // Try to reuse existing connection first
                PluginLog.Verbose("[WS] Checking for existing connection to reuse...");
                if (await this.TryReuseExistingConnectionAsync(baseUrl, accessToken, ct).ConfigureAwait(false))
                {
                    var reuseTime = DateTime.UtcNow - startTime;
                    PluginLog.Debug(() => $"[WS] Connection reused successfully in {reuseTime.TotalMilliseconds:F0}ms");
                    return (true, "Connection reused");
                }

                var wsUri = BuildWebSocketUri(baseUrl);
                this.EndpointUri = wsUri;
                PluginLog.Debug(() => $"[WS] Built WebSocket URI: {wsUri}");

                lock (this._gate)
                {
                    this._ws?.Dispose();
                    this._ws = new ClientWebSocket();
                    // Reset message ID counter for new WebSocket session
                    // Home Assistant expects IDs to start from 1 for each new connection
                    this._nextId = InitialMessageId;
                    PluginLog.Verbose($"[WS] Reset message ID counter to {InitialMessageId} for new connection");
                }

                PluginLog.Debug(() => $"[WS] Initiating WebSocket connection to {wsUri}...");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                var connectStart = DateTime.UtcNow;
                await this._ws.ConnectAsync(wsUri, cts.Token).ConfigureAwait(false);
                var connectTime = DateTime.UtcNow - connectStart;
                PluginLog.Debug(() => $"[WS] WebSocket connected in {connectTime.TotalMilliseconds:F0}ms, state: {this._ws.State}");

                // 1) Expect auth_required
                PluginLog.Verbose("[WS] Waiting for auth_required message...");
                var msgStart = DateTime.UtcNow;
                var first = await this.ReceiveTextAsync(cts.Token).ConfigureAwait(false);
                var msgTime = DateTime.UtcNow - msgStart;
                var type = ReadType(first);
                PluginLog.Debug(() => $"[WS] First message received in {msgTime.TotalMilliseconds:F0}ms: type='{type}'");

                if (!String.Equals(type, "auth_required", StringComparison.OrdinalIgnoreCase))
                {
                    PluginLog.Error(() => $"[WS] Authentication failed - Expected 'auth_required', got '{type}'. Message: {first}");
                    return (false, $"Unexpected first message '{type}'");
                }

                // 2) Send auth
                PluginLog.Verbose("[WS] Sending authentication message...");
                var auth = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
                var authSendStart = DateTime.UtcNow;
                await this.SendTextAsync(auth, cts.Token).ConfigureAwait(false);
                var authSendTime = DateTime.UtcNow - authSendStart;
                PluginLog.Verbose($"[WS] Auth message sent in {authSendTime.TotalMilliseconds:F0}ms");

                // 3) Expect auth_ok
                PluginLog.Verbose("[WS] Waiting for authentication response...");
                var authReplyStart = DateTime.UtcNow;
                var authReply = await this.ReceiveTextAsync(cts.Token).ConfigureAwait(false);
                var authReplyTime = DateTime.UtcNow - authReplyStart;
                var authType = ReadType(authReply);
                PluginLog.Debug(() => $"[WS] Auth response received in {authReplyTime.TotalMilliseconds:F0}ms: type='{authType}'");

                if (String.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
                {
                    this.IsAuthenticated = true;
                    this._lastBaseUrl = baseUrl;
                    this._lastAccessToken = accessToken;

                    var totalTime = DateTime.UtcNow - startTime;
                    PluginLog.Info(() => $"[WS] Authentication successful ✓ - Total time: {totalTime.TotalMilliseconds:F0}ms");
                    return (true, "Authenticated");
                }

                if (String.Equals(authType, "auth_invalid", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = ReadField(authReply, "message") ?? "auth_invalid";
                    PluginLog.Warning(() => $"[WS] Authentication failed - HA returned auth_invalid: {msg}");
                    PluginLog.Verbose($"[WS] Full auth_invalid response: {authReply}");
                    await this.SafeCloseAsync();
                    return (false, $"Authentication failed: {msg}");
                }

                // Anything else is unexpected
                PluginLog.Error(() => $"[WS] Authentication failed - Unexpected auth response type '{authType}'. Full response: {authReply}");
                await this.SafeCloseAsync();
                return (false, "Unexpected auth response");
            }
            catch (OperationCanceledException)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Warning(() => $"[WS] Authentication timeout after {elapsed.TotalSeconds:F1}s - Home Assistant may be unreachable");
                await this.SafeCloseAsync();
                return (false, "Timeout waiting for HA response");
            }
            catch (WebSocketException ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Error(ex, () => $"[WS] WebSocket error during authentication after {elapsed.TotalSeconds:F1}s - WebSocket State: {this._ws?.State}");
                await this.SafeCloseAsync();
                return (false, $"WebSocket error: {ex.Message}");
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Error(ex, () => $"[WS] Unexpected error during authentication after {elapsed.TotalSeconds:F1}s");
                await this.SafeCloseAsync();
                return (false, $"Unexpected error: {ex.Message}");
            }
        }

        public async Task SendPingAsync(CancellationToken ct)
        {
            if (!this.IsAuthenticated || this._ws?.State != WebSocketState.Open)
            {
                PluginLog.Verbose(() => $"[WS] SendPing skipped - Authenticated: {this.IsAuthenticated}, State: {this._ws?.State}");
                return;
            }

            var id = Interlocked.Increment(ref this._nextId);
            var ping = JsonSerializer.Serialize(new { id, type = "ping" });
            PluginLog.Verbose(() => $"[WS] Sending ping with id={id}");
            try
            {
                await this.SendTextAsync(ping, ct).ConfigureAwait(false);
                PluginLog.Verbose($"[WS] Ping sent successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[WS] Failed to send ping with id={id}");
                throw;
            }
        }

        public async Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (String.IsNullOrWhiteSpace(this._lastBaseUrl) || String.IsNullOrWhiteSpace(this._lastAccessToken))
            {
                return false;
            }

            // Try to reuse existing connection with health check
            if (await this.TryReuseExistingConnectionAsync(this._lastBaseUrl, this._lastAccessToken, ct).ConfigureAwait(false))
            {
                return true;
            }

            // If reuse failed, establish new connection
            var (ok, _) = await this.ConnectAndAuthenticateAsync(this._lastBaseUrl, this._lastAccessToken, timeout, ct);
            return ok;
        }

        private async Task<Boolean> TryReuseExistingConnectionAsync(String baseUrl, String accessToken, CancellationToken ct)
        {
            PluginLog.Verbose("[WS] TryReuseExistingConnection - Checking connection reusability...");

            // Check if we have a connection to the same endpoint with same credentials
            if (this._ws?.State != WebSocketState.Open || !this.IsAuthenticated)
            {
                PluginLog.Verbose(() => $"[WS] Connection not reusable - State: {this._ws?.State}, Authenticated: {this.IsAuthenticated}");
                return false;
            }

            // Check if the connection parameters match
            var urlMatch = String.Equals(this._lastBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
            var tokenMatch = String.Equals(this._lastAccessToken, accessToken, StringComparison.Ordinal);

            if (!urlMatch || !tokenMatch)
            {
                PluginLog.Verbose(() => $"[WS] Connection not reusable - URL match: {urlMatch}, Token match: {tokenMatch}");
                return false;
            }

            PluginLog.Verbose("[WS] Connection parameters match, testing health...");

            // Test connection health with a ping
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(HealthCheckTimeoutSeconds)); // Short timeout for health check

                var healthStart = DateTime.UtcNow;
                await this.SendPingAsync(cts.Token).ConfigureAwait(false);

                // Wait briefly for a pong response (optional - ping is fire-and-forget in HA)
                await Task.Delay(PongResponseDelayMs, cts.Token).ConfigureAwait(false);

                var healthTime = DateTime.UtcNow - healthStart;
                PluginLog.Debug(() => $"[WS] Connection reuse successful - Health check passed in {healthTime.TotalMilliseconds:F0}ms");
                return true;
            }
            catch (OperationCanceledException)
            {
                PluginLog.Info("[WS] Connection reuse failed - Health check timeout, connection may be stale");
                return false;
            }
            catch (WebSocketException ex)
            {
                PluginLog.Debug(() => $"[WS] Connection reuse failed - WebSocket error during health check: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[WS] Connection reuse failed - Unexpected error during health check");
                return false;
            }
        }




        public async Task<(Boolean ok, String? resultJson, String? errorMessage)> RequestAsync(
        String type, CancellationToken ct)
        {
            if (!this.IsAuthenticated)
            {
                return (false, null, "Not authenticated");
            }

            var id = Interlocked.Increment(ref this._nextId);

            var payload = JsonSerializer.Serialize(new { id, type });
            await this.SendTextAsync(payload, ct).ConfigureAwait(false);

            // Wait for matching { "id": id, "type": "result", ... }
            while (true)
            {
                var msg = await this.ReceiveTextAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;

                if (!root.TryGetProperty("id", out var idProp) || idProp.GetInt32() != id)
                {
                    continue; // not our response (can be pongs/other)
                }

                var msgType = root.GetProperty("type").GetString();
                if (!String.Equals(msgType, "result", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var success = root.GetProperty("success").GetBoolean();
                if (success)
                {
                    // Some results are arrays/objects; return raw JSON
                    var resultElem = root.GetProperty("result");
                    return (true, resultElem.GetRawText(), null);
                }
                else
                {
                    var error = "Unknown error";
                    if (root.TryGetProperty("error", out var err))
                    {
                        var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                        var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                        error = !String.IsNullOrEmpty(message) ? message : code ?? "Request failed";
                    }
                    return (false, null, error);
                }
            }
        }

        public async Task<(Boolean ok, String? error)> CallServiceAsync(
    String domain, String service, String entityId, JsonElement? data, CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Debug(() => $"[WS] CallServiceAsync START - {domain}.{service} -> {entityId}, hasData: {data.HasValue}");

            if (data.HasValue)
            {
                try
                {
                    var dataStr = data.Value.GetRawText();
                    var truncatedData = dataStr.Length > LogDataTruncationLength ? dataStr.Substring(0, LogDataTruncationLength) + "..." : dataStr;
                    PluginLog.Verbose(() => $"[WS] Service data: {truncatedData}");
                }
                catch (Exception ex)
                {
                    PluginLog.Verbose($"[WS] Could not log service data: {ex.Message}");
                }
            }

            try
            {
                // Ensure we have a live, authed socket
                if (this._ws == null || this._ws.State != WebSocketState.Open || !this.IsAuthenticated)
                {
                    PluginLog.Verbose(() => $"[WS] Connection check failed - State: {this._ws?.State}, Authenticated: {this.IsAuthenticated}, attempting reconnect...");
                    var re = await this.EnsureConnectedAsync(TimeSpan.FromSeconds(ReconnectionTimeoutSeconds), ct).ConfigureAwait(false);
                    if (!re)
                    {
                        PluginLog.Warning(() => $"[WS] CallServiceAsync failed - Could not establish connection for {domain}.{service}");
                        return (false, "connection lost");
                    }
                    PluginLog.Verbose("[WS] Connection re-established successfully");
                }

                var id = Interlocked.Increment(ref this._nextId);
                var obj = new Dictionary<String, Object>
                {
                    ["id"] = id,
                    ["type"] = "call_service",
                    ["domain"] = domain,
                    ["service"] = service,
                    ["target"] = new Dictionary<String, Object> { ["entity_id"] = entityId },
                };
                if (data.HasValue)
                {
                    obj["service_data"] = data.Value;
                }

                var json = JsonSerializer.Serialize(obj);
                
                // Log the complete command being sent to Home Assistant
                PluginLog.Info($"[HA-CMD] Sending command to Home Assistant: {json}");
                
                await this.SendTextAsync(json, ct).ConfigureAwait(false);

                var sendStart = DateTime.UtcNow;
                await this.SendTextAsync(json, ct).ConfigureAwait(false);
                var sendTime = DateTime.UtcNow - sendStart;
                PluginLog.Verbose(() => $"[WS] Service call sent in {sendTime.TotalMilliseconds:F0}ms, waiting for response...");

                var responseStart = DateTime.UtcNow;
                while (true)
                {
                    var msg = await this.ReceiveTextAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idProp) || idProp.GetInt32() != id)
                    {
                        PluginLog.Trace(() => $"[WS] Ignoring message with different id (expected {id})");
                        continue;
                    }

                    if (!String.Equals(root.GetProperty("type").GetString(), "result", StringComparison.OrdinalIgnoreCase))
                    {
                        PluginLog.Trace(() => $"[WS] Ignoring non-result message for id {id}");
                        continue;
                    }

                    var responseTime = DateTime.UtcNow - responseStart;
                    var totalTime = DateTime.UtcNow - startTime;
                    var success = root.GetProperty("success").GetBoolean();

                    if (success)
                    {
                        PluginLog.Info(() => $"[WS] CallServiceAsync SUCCESS - {domain}.{service} -> {entityId} completed in {totalTime.TotalMilliseconds:F0}ms (response: {responseTime.TotalMilliseconds:F0}ms)");
                        return (true, null);
                    }
                    else
                    {
                        // Extract specific error details from the response
                        var error = "call_service failed";
                        if (root.TryGetProperty("error", out var errorElement))
                        {
                            var code = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
                            var message = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;

                            if (!String.IsNullOrEmpty(message))
                            {
                                error = !String.IsNullOrEmpty(code) ? $"{code}: {message}" : message;
                            }
                            else if (!String.IsNullOrEmpty(code))
                            {
                                error = $"Error code: {code}";
                            }
                        }

                        PluginLog.Warning(() => $"[WS] CallServiceAsync FAILED - {domain}.{service} -> {entityId} failed in {totalTime.TotalMilliseconds:F0}ms: {error}");
                        PluginLog.Verbose($"[WS] Full error response: {root.GetRawText()}");
                        return (false, error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Warning(() => $"[WS] CallServiceAsync TIMEOUT - {domain}.{service} -> {entityId} timed out after {elapsed.TotalSeconds:F1}s");
                return (false, "timeout");
            }
            catch (WebSocketException ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Warning(ex, () => $"[WS] CallServiceAsync WEBSOCKET_ERROR - {domain}.{service} -> {entityId} failed after {elapsed.TotalSeconds:F1}s");
                await this.SafeCloseAsync().ConfigureAwait(false);
                return (false, "connection lost");
            }
            catch (IOException ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Warning(ex, () => $"[WS] CallServiceAsync IO_ERROR - {domain}.{service} -> {entityId} failed after {elapsed.TotalSeconds:F1}s");
                await this.SafeCloseAsync().ConfigureAwait(false);
                return (false, "connection lost");
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Error(ex, () => $"[WS] CallServiceAsync UNEXPECTED_ERROR - {domain}.{service} -> {entityId} failed after {elapsed.TotalSeconds:F1}s");
                return (false, "error");
            }
        }




        public async Task SafeCloseAsync()
        {
            PluginLog.Verbose(() => $"[WS] SafeCloseAsync - Current state: {this._ws?.State}, Authenticated: {this.IsAuthenticated}");

            try
            {
                if (this._ws != null && this._ws.State == WebSocketState.Open)
                {
                    PluginLog.Verbose("[WS] Sending close frame to Home Assistant...");
                    var closeStart = DateTime.UtcNow;
                    await this._ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                    var closeTime = DateTime.UtcNow - closeStart;
                    PluginLog.Debug(() => $"[WS] WebSocket closed gracefully in {closeTime.TotalMilliseconds:F0}ms");
                }
                else
                {
                    PluginLog.Verbose(() => $"[WS] WebSocket not open (State: {this._ws?.State}), skipping close frame");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[WS] Error sending close frame - proceeding with cleanup");
            }
            finally
            {
                lock (this._gate)
                {
                    this._ws?.Dispose();
                    this._ws = null;
                    this.IsAuthenticated = false;
                    // Reset message ID counter when closing connection
                    // This ensures fresh ID sequence for next connection
                    this._nextId = InitialMessageId;
                    PluginLog.Verbose($"[WS] Reset message ID counter to {InitialMessageId} for next connection");
                }
                PluginLog.Info("[WS] WebSocket client disposed and reset");
            }
        }

        // Implement IAsyncDisposable for proper async resource cleanup
        public async ValueTask DisposeAsync()
        {
            await this.SafeCloseAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        // Synchronous dispose for backwards compatibility
        public void Dispose()
        {
            // Use GetAwaiter().GetResult() to avoid deadlocks in sync contexts
            this.SafeCloseAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        // ---- helpers ----

        private static Uri BuildWebSocketUri(String baseUrl)
        {
            // Accept http://host[:port][/path] or https://...
            var uri = new Uri(baseUrl.TrimEnd('/'));
            var builder = new UriBuilder(uri);
            builder.Scheme = (uri.Scheme == "https") ? "wss" : "ws";
            builder.Path = String.Join("/", uri.AbsolutePath.TrimEnd('/'), "api", "websocket");
            return builder.Uri;
        }

        private async Task<String> ReceiveTextAsync(CancellationToken ct)
        {
            if (this._ws == null)
            {
                throw new InvalidOperationException("WebSocket is not initialized");
            }

            var buffer = new ArraySegment<Byte>(new Byte[WebSocketBufferSize]);
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await this._ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Server closed connection");
                }

                if (buffer.Array != null)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, result.Count));
                }
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        private Task SendTextAsync(String text, CancellationToken ct)
        {
            if (this._ws == null)
            {
                throw new InvalidOperationException("WebSocket is not initialized");
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            return this._ws.SendAsync(new ArraySegment<Byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static String? ReadType(String json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }

        private static String? ReadField(String json, String name)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
        }
    }
}
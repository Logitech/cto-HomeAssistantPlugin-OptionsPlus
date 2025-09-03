namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class HaWebSocketClient : IDisposable
    {
        private ClientWebSocket _ws;
        private readonly Object _gate = new();
        private Int32 _nextId = 1;
        public Boolean IsAuthenticated { get; private set; }
        public Uri EndpointUri { get; private set; }
        
        private String _lastBaseUrl;
        private String _lastAccessToken;


        public async Task<(Boolean ok, String message)> ConnectAndAuthenticateAsync(
            String baseUrl, String accessToken, TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(baseUrl))
                {
                    return (false, "Base URL is empty");
                }

                if (String.IsNullOrWhiteSpace(accessToken))
                {
                    return (false, "Access token is empty");
                }

                var wsUri = BuildWebSocketUri(baseUrl);
                this.EndpointUri = wsUri;

                lock (this._gate)
                {
                    this._ws?.Dispose();
                    this._ws = new ClientWebSocket();


                }

                PluginLog.Info($"Connecting to {wsUri}");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                await this._ws.ConnectAsync(wsUri, cts.Token).ConfigureAwait(false);

                // 1) Expect auth_required
                var first = await this.ReceiveTextAsync(cts.Token).ConfigureAwait(false);
                var type = ReadType(first);
                PluginLog.Info($"First message: {type}");

                if (!String.Equals(type, "auth_required", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Unexpected first message '{type}'");
                }

                // 2) Send auth
                var auth = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
                await this.SendTextAsync(auth, cts.Token).ConfigureAwait(false);

                // 3) Expect auth_ok
                var authReply = await this.ReceiveTextAsync(cts.Token).ConfigureAwait(false);
                var authType = ReadType(authReply);

                if (String.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
                {
                    this.IsAuthenticated = true;
                    this._lastBaseUrl   = baseUrl;
                    this._lastAccessToken = accessToken;

                    PluginLog.Info("HA auth_ok ✔");
                    return (true, "Authenticated");
                }

                if (String.Equals(authType, "auth_invalid", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = ReadField(authReply, "message") ?? "auth_invalid";
                    PluginLog.Warning($"HA auth_invalid: {msg}");
                    await this.SafeCloseAsync();
                    return (false, $"Authentication failed: {msg}");
                }

                // Anything else is unexpected
                PluginLog.Warning($"Unexpected auth response: {authReply}");
                await this.SafeCloseAsync();
                return (false, "Unexpected auth response");
            }
            catch (OperationCanceledException)
            {
                PluginLog.Warning("HA WS auth timeout");
                await this.SafeCloseAsync();
                return (false, "Timeout waiting for HA response");
            }
            catch (WebSocketException ex)
            {
                PluginLog.Error(ex, "WebSocket error during HA auth");
                await this.SafeCloseAsync();
                return (false, $"WebSocket error: {ex.Message}");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Unexpected error during HA auth");
                await this.SafeCloseAsync();
                return (false, $"Unexpected error: {ex.Message}");
            }
        }

        public async Task SendPingAsync(CancellationToken ct)
        {
            if (!this.IsAuthenticated || this._ws?.State != WebSocketState.Open)
            {
                return;
            }

            var id = Interlocked.Increment(ref this._nextId);
            var ping = JsonSerializer.Serialize(new { id, type = "ping" });
            await this.SendTextAsync(ping, ct).ConfigureAwait(false);
        }

        public async Task<Boolean> EnsureConnectedAsync(TimeSpan timeout, CancellationToken ct)
        {
            if (this._ws != null && this._ws.State == WebSocketState.Open && this.IsAuthenticated)
            {
                return true;
            }

            if (String.IsNullOrWhiteSpace(this._lastBaseUrl) || String.IsNullOrWhiteSpace(this._lastAccessToken))
            {
                return false;
            }

            var (ok, _) = await this.ConnectAndAuthenticateAsync(this._lastBaseUrl, this._lastAccessToken, timeout, ct);
            return ok;
        }




        public async Task<(Boolean ok, String resultJson, String errorMessage)> RequestAsync(
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

        public async Task<(Boolean ok, String error)> CallServiceAsync(
    String domain, String service, String entityId, JsonElement? data, CancellationToken ct)
        {
            try
            {
                // Ensure we have a live, authed socket
                if (this._ws == null || this._ws.State != WebSocketState.Open || !this.IsAuthenticated)
                {
                    var re = await this.EnsureConnectedAsync(TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
                    if (!re)
                    {
                        return (false, "connection lost");
                    }
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
                await this.SendTextAsync(json, ct).ConfigureAwait(false);

                while (true)
                {
                    var msg = await this.ReceiveTextAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idProp) || idProp.GetInt32() != id)
                    {
                        continue;
                    }

                    if (!String.Equals(root.GetProperty("type").GetString(), "result", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var success = root.GetProperty("success").GetBoolean();
                    return success ? (true, null) : (false, "call_service failed");
                }
            }
            catch (OperationCanceledException)
            {
                // timeout from your CTS -> NOT fatal; keep socket
                return (false, "timeout");
            }
            catch (WebSocketException)
            {
                await this.SafeCloseAsync().ConfigureAwait(false);
                return (false, "connection lost");
            }
            catch (IOException)
            {
                await this.SafeCloseAsync().ConfigureAwait(false);
                return (false, "connection lost");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "CallServiceAsync unexpected error");
                return (false, "error");
            }
        }




        public async Task SafeCloseAsync()
        {
            try
            {
                if (this._ws != null && this._ws.State == WebSocketState.Open)
                {
                    await this._ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Error closing WS");
            }
            finally
            {
                lock (this._gate)
                {
                    this._ws?.Dispose();
                    this._ws = null;
                    this.IsAuthenticated = false;
                }
            }
        }

        public void Dispose() => _ = this.SafeCloseAsync();

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
            var buffer = new ArraySegment<Byte>(new Byte[8192]);
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await this._ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Server closed connection");
                }

                sb.Append(Encoding.UTF8.GetString(buffer.Array, 0, result.Count));
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        private Task SendTextAsync(String text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return this._ws.SendAsync(new ArraySegment<Byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static String ReadType(String json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }

        private static String ReadField(String json, String name)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
        }
    }
}

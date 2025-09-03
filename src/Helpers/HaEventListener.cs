namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class HaEventListener : IDisposable
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _loop;
        private int _nextId = 1;

        public event Action<string,int?> BrightnessChanged; // (entityId, brightness 0..255 or null)
        public event Action<string, int?, int?, int?, int?> ColorTempChanged;
        // args: (entityId, mired, kelvin, min_mireds, max_mireds)



        public event Action<string, double?, double?> HsColorChanged; // (entityId, hue 0..360, sat 0..100)
        public event Action<string, bool> ScriptRunningChanged; // (entityId, isRunning)



        public async Task<bool> ConnectAndSubscribeAsync(string baseUrl, string accessToken, CancellationToken ct)
        {
            try
            {
                var wsUri = BuildWebSocketUri(baseUrl);
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                await _ws.ConnectAsync(wsUri, ct);

                // 1) auth_required
                var first = await ReceiveTextAsync(ct);
                var type = ReadType(first);
                if (!string.Equals(type, "auth_required", StringComparison.OrdinalIgnoreCase))
                    return false;

                // 2) auth
                var auth = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
                await SendTextAsync(auth, ct);

                // 3) auth_ok
                var authReply = await ReceiveTextAsync(ct);
                var authType = ReadType(authReply);
                if (!string.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
                    return false;

                // 4) subscribe_events: state_changed
                var id = Interlocked.Increment(ref _nextId);
                var sub = JsonSerializer.Serialize(new { id, type = "subscribe_events", event_type = "state_changed" });
                await SendTextAsync(sub, ct);

                // 5) expect result success for subscription
                var subReply = await ReceiveTextAsync(ct);
                using (var doc = JsonDocument.Parse(subReply))
                {
                    var root = doc.RootElement;
                    if (!(root.TryGetProperty("type", out var t) && t.GetString() == "result"
                          && root.TryGetProperty("id", out var rid) && rid.GetInt32() == id
                          && root.TryGetProperty("success", out var s) && s.GetBoolean()))
                    {
                        return false;
                    }
                }

                _cts?.Cancel();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[events] connect/subscribe failed");
                await SafeCloseAsync();
                return false;
            }
        }

     
    // Local helper to compute Kelvin when HA only gives mireds
    private async Task ReceiveLoopAsync(CancellationToken ct)
{
    // Local helper to compute Kelvin when HA only gives mireds
    static int MiredToKelvinSafe(int m) => (int)Math.Round(1_000_000.0 / Math.Max(1, m));

    try
    {
        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            var msg = await ReceiveTextAsync(ct);
            using var doc = JsonDocument.Parse(msg);
            var root = doc.RootElement;

            // Only care about event frames
            if (!root.TryGetProperty("type", out var t) || t.GetString() != "event")
                continue;

            // Must be a state_changed event
            if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
                continue;
            if (!ev.TryGetProperty("event_type", out var et) || et.GetString() != "state_changed")
                continue;
            if (!ev.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                continue;

            var entityId = data.TryGetProperty("entity_id", out var idProp) ? idProp.GetString() : null;
            if (String.IsNullOrEmpty(entityId))
                continue;

            // NEW/UPDATED STATE
            int? bri = null;
            int? ctMired = null;
            int? ctKelvin = null;
            int? minMireds = null;
            int? maxMireds = null;

            // HS color
            double? hue = null, sat = null;

            // Generic ON/OFF (used for scripts toggle state)
            bool? isOn = null;

            if (data.TryGetProperty("new_state", out var ns) && ns.ValueKind == JsonValueKind.Object)
            {
                // attributes (may be missing depending on integration/state)
                if (ns.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                {
                    // Brightness 0..255
                    if (attrs.TryGetProperty("brightness", out var br) && br.ValueKind == JsonValueKind.Number)
                    {
                        bri = HSBHelper.Clamp(br.GetInt32(), 0, 255);
                    }

                    // Color temperature (mireds + (optional) kelvin, plus bounds)
                    if (attrs.TryGetProperty("color_temp", out var ctT) && ctT.ValueKind == JsonValueKind.Number)
                    {
                        ctMired = ctT.GetInt32();
                    }
                    if (attrs.TryGetProperty("color_temp_kelvin", out var ctk) && ctk.ValueKind == JsonValueKind.Number)
                    {
                        ctKelvin = ctk.GetInt32();
                    }
                    if (attrs.TryGetProperty("min_mireds", out var minM) && minM.ValueKind == JsonValueKind.Number)
                    {
                        minMireds = minM.GetInt32();
                    }
                    if (attrs.TryGetProperty("max_mireds", out var maxM) && maxM.ValueKind == JsonValueKind.Number)
                    {
                        maxMireds = maxM.GetInt32();
                    }

                    // HS color (Hue/Saturation)
                    if (attrs.TryGetProperty("hs_color", out var hs) &&
                        hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() >= 2)
                    {
                        if (hs[0].ValueKind == JsonValueKind.Number)
                            hue = hs[0].GetDouble(); // 0..360
                        if (hs[1].ValueKind == JsonValueKind.Number)
                            sat = hs[1].GetDouble(); // 0..100
                    }
                }

                // Generic state
                if (ns.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String)
                {
                    var stStr = st.GetString();
                    isOn = String.Equals(stStr, "on", StringComparison.OrdinalIgnoreCase);

                    // If light is OFF, normalize brightness to 0 (HA often omits brightness then)
                    if (String.Equals(stStr, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        bri = 0;
                        // For temp/hs when OFF: leave as-is (null) so UI can keep last known;
                        // some lights don't report those attributes while off.
                    }
                }
            }

            // If we only got mireds OR only kelvin, derive the other for convenience
            if (!ctKelvin.HasValue && ctMired.HasValue)
                ctKelvin = MiredToKelvinSafe(ctMired.Value);
            if (!ctMired.HasValue && ctKelvin.HasValue && ctKelvin.Value > 0)
                ctMired = (int)Math.Round(1_000_000.0 / ctKelvin.Value);

            // Fire events (individually guarded so one can't break the other)
            try { BrightnessChanged?.Invoke(entityId, bri); } catch { /* keep loop alive */ }
            try { ColorTempChanged?.Invoke(entityId, ctMired, ctKelvin, minMireds, maxMireds); } catch { /* keep loop alive */ }
            try { HsColorChanged?.Invoke(entityId, hue, sat); } catch { /* keep loop alive */ }

            // NEW: script running state (on = running, off = idle)
            try
            {
                if (entityId.StartsWith("script.", StringComparison.OrdinalIgnoreCase) && isOn.HasValue)
                {
                    ScriptRunningChanged?.Invoke(entityId, isOn.Value);
                }
            }
            catch { /* keep loop alive */ }
        }
    }
    catch (OperationCanceledException) { /* normal on shutdown */ }
    catch (WebSocketException) { /* connection dropped; outer code will handle */ }
    catch (Exception ex)
    {
        PluginLog.Warning(ex, "[events] receive loop crashed");
    }
}




        public async Task SafeCloseAsync()
        {
            try
            {
                _cts?.Cancel();
                if (_ws?.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
            }
            catch { }
            finally
            {
                _ws?.Dispose();
                _ws = null;
            }
        }

        public void Dispose() => _ = SafeCloseAsync();

        // --- helpers ---
        private static Uri BuildWebSocketUri(string baseUrl)
        {
            var uri = new Uri(baseUrl.TrimEnd('/'));
            var builder = new UriBuilder(uri)
            {
                Scheme = (uri.Scheme == "https") ? "wss" : "ws",
                Path = string.Join("/", uri.AbsolutePath.TrimEnd('/'), "api", "websocket")
            };
            return builder.Uri;
        }

        private async Task<string> ReceiveTextAsync(CancellationToken ct)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Server closed");
                sb.Append(Encoding.UTF8.GetString(buffer.Array, 0, result.Count));
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        private Task SendTextAsync(string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private static string ReadType(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
    }
}

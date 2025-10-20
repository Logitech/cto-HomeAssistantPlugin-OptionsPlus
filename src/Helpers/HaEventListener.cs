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
        // ====================================================================
        // CONSTANTS - Home Assistant Event Listener Configuration
        // ====================================================================

        // --- Message ID Constants ---
        private const Int32 InitialMessageId = 1;                     // Initial message ID for WebSocket communication

        // --- Color Value Constants ---
        private const Double KelvinMiredConversionFactor = 1_000_000.0; // Conversion factor: Kelvin × Mired = 1,000,000
        private const Int32 MinSafeTemperatureValue = 1;               // Minimum safe temperature value to prevent division by zero

        // --- Brightness Constants ---
        private const Int32 MinBrightnessValue = 0;                    // Minimum brightness value (off)
        private const Int32 MaxBrightnessValue = 255;                  // Maximum brightness value (full brightness)
        private const Int32 BrightnessOffValue = 0;                    // Brightness value when light is off

        // --- Color Array Length Constants ---
        private const Int32 HsColorArrayMinLength = 2;                 // Minimum length for HS color arrays [hue, saturation]
        private const Int32 RgbColorArrayMinLength = 3;                // Minimum length for RGB color arrays [red, green, blue]
        private const Int32 XyColorArrayMinLength = 2;                 // Minimum length for XY color arrays [x, y]

        // --- Color Component Array Indices ---
        private const Int32 HueArrayIndex = 0;                         // Array index for hue component in HS color
        private const Int32 SaturationArrayIndex = 1;                  // Array index for saturation component in HS color
        private const Int32 RedArrayIndex = 0;                         // Array index for red component in RGB color
        private const Int32 GreenArrayIndex = 1;                       // Array index for green component in RGB color
        private const Int32 BlueArrayIndex = 2;                        // Array index for blue component in RGB color
        private const Int32 XCoordinateIndex = 0;                      // Array index for X coordinate in XY color
        private const Int32 YCoordinateIndex = 1;                      // Array index for Y coordinate in XY color

        // --- Buffer Constants ---
        private const Int32 WebSocketBufferSize = 8192;                // Buffer size for WebSocket receive operations

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private Int32 _nextId = InitialMessageId;

        public event Action<String, Int32?>? BrightnessChanged; // (entityId, brightness 0..255 or null)
        public event Action<String, Int32?, Int32?, Int32?, Int32?>? ColorTempChanged;
        // args: (entityId, mired, kelvin, min_mireds, max_mireds)



        public event Action<String, Double?, Double?>? HsColorChanged; // (entityId, hue 0..360, sat 0..100)
        public event Action<String, Int32?, Int32?, Int32?>? RgbColorChanged;
        public event Action<String, Double?, Double?, Int32?>? XyColorChanged;

        public event Action<String, Boolean>? ScriptRunningChanged; // (entityId, isRunning)



        public async Task<Boolean> ConnectAndSubscribeAsync(String baseUrl, String accessToken, CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;
            PluginLog.Debug(() => $"[Events] ConnectAndSubscribeAsync START - baseUrl: '{baseUrl}'");

            try
            {
                var wsUri = BuildWebSocketUri(baseUrl);
                PluginLog.Debug(() => $"[Events] Built WebSocket URI: {wsUri}");

                this._ws?.Dispose();
                this._ws = new ClientWebSocket();

                PluginLog.Verbose("[Events] Initiating WebSocket connection...");
                var connectStart = DateTime.UtcNow;
                await this._ws.ConnectAsync(wsUri, ct);
                var connectTime = DateTime.UtcNow - connectStart;
                PluginLog.Debug(() => $"[Events] WebSocket connected in {connectTime.TotalMilliseconds:F0}ms");

                // 1) auth_required
                PluginLog.Verbose("[Events] Waiting for auth_required message...");
                var first = await this.ReceiveTextAsync(ct);
                var type = ReadType(first);
                PluginLog.Debug(() => $"[Events] First message received: type='{type}'");

                if (!String.Equals(type, "auth_required", StringComparison.OrdinalIgnoreCase))
                {
                    PluginLog.Error(() => $"[Events] Authentication failed - Expected 'auth_required', got '{type}'");
                    return false;
                }

                // 2) auth
                PluginLog.Verbose("[Events] Sending authentication...");
                var auth = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
                await this.SendTextAsync(auth, ct);

                // 3) auth_ok
                PluginLog.Verbose("[Events] Waiting for auth response...");
                var authReply = await this.ReceiveTextAsync(ct);
                var authType = ReadType(authReply);
                PluginLog.Debug(() => $"[Events] Auth response: type='{authType}'");

                if (!String.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
                {
                    PluginLog.Error(() => $"[Events] Authentication failed - Got '{authType}' instead of 'auth_ok'");
                    return false;
                }

                // 4) subscribe_events: state_changed
                var id = Interlocked.Increment(ref this._nextId);
                PluginLog.Debug(() => $"[Events] Subscribing to state_changed events with id={id}...");
                var sub = JsonSerializer.Serialize(new { id, type = "subscribe_events", event_type = "state_changed" });
                await this.SendTextAsync(sub, ct);

                // 5) expect result success for subscription
                var subReply = await this.ReceiveTextAsync(ct);
                using (var doc = JsonDocument.Parse(subReply))
                {
                    var root = doc.RootElement;
                    var isResult = root.TryGetProperty("type", out var t) && t.GetString() == "result";
                    var matchesId = root.TryGetProperty("id", out var rid) && rid.GetInt32() == id;
                    var isSuccess = root.TryGetProperty("success", out var s) && s.GetBoolean();

                    PluginLog.Debug(() => $"[Events] Subscription response - isResult: {isResult}, matchesId: {matchesId}, isSuccess: {isSuccess}");

                    if (!(isResult && matchesId && isSuccess))
                    {
                        PluginLog.Error(() => $"[Events] Subscription failed - Response: {subReply}");
                        return false;
                    }
                }

                this._cts?.Cancel();
                this._cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                this._loop = Task.Run(() => this.ReceiveLoopAsync(this._cts.Token));

                var totalTime = DateTime.UtcNow - startTime;
                PluginLog.Info(() => $"[Events] ConnectAndSubscribeAsync SUCCESS - Event listener ready in {totalTime.TotalMilliseconds:F0}ms");
                return true;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Error(ex, () => $"[Events] ConnectAndSubscribeAsync FAILED after {elapsed.TotalSeconds:F1}s");
                await this.SafeCloseAsync();
                return false;
            }
        }


        // Local helper to compute Kelvin when HA only gives mireds
        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            // Local helper to compute Kelvin when HA only gives mireds
            static Int32 MiredToKelvinSafe(Int32 m) => (Int32)Math.Round(KelvinMiredConversionFactor / Math.Max(MinSafeTemperatureValue, m));

            try
            {
                while (!ct.IsCancellationRequested && this._ws?.State == WebSocketState.Open)
                {
                    var msg = await this.ReceiveTextAsync(ct);
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;

                    // Only care about event frames
                    if (!root.TryGetProperty("type", out var t) || t.GetString() != "event")
                    {
                        continue;
                    }

                    // Must be a state_changed event
                    if (!root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!ev.TryGetProperty("event_type", out var et) || et.GetString() != "state_changed")
                    {
                        continue;
                    }

                    if (!ev.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var entityId = data.TryGetProperty("entity_id", out var idProp) ? idProp.GetString() : null;
                    if (String.IsNullOrEmpty(entityId))
                    {
                        continue;
                    }

                    if (entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        PluginLog.Trace(() => $"[ReceiveLoopAsync]{entityId} frame received");
                    }


                    // NEW/UPDATED STATE
                    Int32? bri = null;
                    Int32? ctMired = null;
                    Int32? ctKelvin = null;
                    Int32? minMireds = null;
                    Int32? maxMireds = null;

                    // HS color
                    Double? hue = null, sat = null;

                    // NEW: RGB and XY
                    Int32? rgbR = null, rgbG = null, rgbB = null;
                    Double? xyX = null, xyY = null;

                    // Generic ON/OFF (used for scripts toggle state)
                    Boolean? isOn = null;

                    if (data.TryGetProperty("new_state", out var ns) && ns.ValueKind == JsonValueKind.Object)
                    {
                        // attributes (may be missing depending on integration/state)
                        if (ns.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                        {
                            // Brightness 0..255
                            if (attrs.TryGetProperty("brightness", out var br) && br.ValueKind == JsonValueKind.Number)
                            {
                                bri = HSBHelper.Clamp(br.GetInt32(), MinBrightnessValue, MaxBrightnessValue);
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
                                hs.ValueKind == JsonValueKind.Array && hs.GetArrayLength() >= HsColorArrayMinLength)
                            {
                                if (hs[HueArrayIndex].ValueKind == JsonValueKind.Number)
                                {
                                    hue = hs[HueArrayIndex].GetDouble(); // 0..360
                                }

                                if (hs[SaturationArrayIndex].ValueKind == JsonValueKind.Number)
                                {
                                    sat = hs[SaturationArrayIndex].GetDouble(); // 0..100
                                }
                            }

                            // NEW: RGB color
                            if (attrs.TryGetProperty("rgb_color", out var rgb) &&
                                rgb.ValueKind == JsonValueKind.Array && rgb.GetArrayLength() >= RgbColorArrayMinLength &&
                                rgb[RedArrayIndex].ValueKind == JsonValueKind.Number &&
                                rgb[GreenArrayIndex].ValueKind == JsonValueKind.Number &&
                                rgb[BlueArrayIndex].ValueKind == JsonValueKind.Number)
                            {
                                rgbR = rgb[RedArrayIndex].GetInt32();
                                rgbG = rgb[GreenArrayIndex].GetInt32();
                                rgbB = rgb[BlueArrayIndex].GetInt32();
                            }

                            // NEW: XY color
                            if (attrs.TryGetProperty("xy_color", out var xy) &&
                                xy.ValueKind == JsonValueKind.Array && xy.GetArrayLength() >= XyColorArrayMinLength &&
                                xy[XCoordinateIndex].ValueKind == JsonValueKind.Number &&
                                xy[YCoordinateIndex].ValueKind == JsonValueKind.Number)
                            {
                                xyX = xy[XCoordinateIndex].GetDouble();
                                xyY = xy[YCoordinateIndex].GetDouble();
                                // brightness is taken from 'bri' above if present; if not present, handler can fallback
                            }
                        }

                        if (bri.HasValue)
                        {
                            PluginLog.Trace(() => $" bri={bri} eid={entityId}");
                        }

                        if (hue.HasValue || sat.HasValue)
                        {
                            PluginLog.Trace(() => $"hs=[{hue?.ToString("F1") ?? "-"},{sat?.ToString("F1") ?? "-"}] eid={entityId}");
                        }

                        if (rgbR.HasValue)
                        {
                            PluginLog.Trace(() => $"rgb=[{rgbR},{rgbG},{rgbB}] eid={entityId}");
                        }

                        if (xyX.HasValue)
                        {
                            PluginLog.Trace(() => $" xy=[{xyX:F4},{xyY:F4}] eid={entityId}");
                        }

                        if (ctMired.HasValue || ctKelvin.HasValue)
                        {
                            PluginLog.Trace(() => $"ct={ctMired}mired/{ctKelvin}K min={minMireds} max={maxMireds} eid={entityId}");
                        }


                        // Generic state
                        if (ns.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String)
                        {
                            var stStr = st.GetString();
                            isOn = String.Equals(stStr, "on", StringComparison.OrdinalIgnoreCase);

                            // If light is OFF, normalize brightness to 0 (HA often omits brightness then)
                            if (String.Equals(stStr, "off", StringComparison.OrdinalIgnoreCase))
                            {
                                bri = BrightnessOffValue;
                                // For temp/hs when OFF: leave as-is (null) so UI can keep last known;
                                // some lights don't report those attributes while off.
                            }
                        }
                    }

                    // If we only got mireds OR only kelvin, derive the other for convenience
                    if (!ctKelvin.HasValue && ctMired.HasValue)
                    {
                        ctKelvin = MiredToKelvinSafe(ctMired.Value);
                    }

                    if (!ctMired.HasValue && ctKelvin.HasValue && ctKelvin.Value > 0)
                    {
                        ctMired = (Int32)Math.Round(KelvinMiredConversionFactor / ctKelvin.Value);
                    }

                    var hasHs = hue.HasValue && sat.HasValue;

                    // Fire events (individually guarded so one can't break the other)
                    try
                    {
                        var briSubs = BrightnessChanged?.GetInvocationList()?.Length ?? 0;
                        PluginLog.Trace(() => $"[EV] BrightnessChanged subscribers={briSubs} eid={entityId}");
                        BrightnessChanged?.Invoke(entityId, bri);
                        PluginLog.Trace(() => $"firing brightness event for {entityId} bri={bri}");
                    }
                    catch { /* keep loop alive */ }
                    try
                    { ColorTempChanged?.Invoke(entityId, ctMired, ctKelvin, minMireds, maxMireds); }
                    catch { /* keep loop alive */ }
                    try
                    { HsColorChanged?.Invoke(entityId, hue, sat); }
                    catch { /* keep loop alive */ }

                    // Only emit RGB/XY when HS not present (avoids duplicate UI work)
                    if (!hasHs)
                    {
                        try
                        { RgbColorChanged?.Invoke(entityId, rgbR, rgbG, rgbB); }
                        catch { }
                        try
                        { XyColorChanged?.Invoke(entityId, xyX, xyY, bri); }
                        catch { }
                    }


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
            PluginLog.Debug(() => $"[Events] SafeCloseAsync - Current state: {this._ws?.State}");

            try
            {
                PluginLog.Verbose("[Events] Canceling event processing loop...");
                this._cts?.Cancel();

                if (this._ws?.State == WebSocketState.Open)
                {
                    PluginLog.Verbose("[Events] Closing WebSocket connection...");
                    var closeStart = DateTime.UtcNow;
                    await this._ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                    var closeTime = DateTime.UtcNow - closeStart;
                    PluginLog.Debug(() => $"[Events] WebSocket closed in {closeTime.TotalMilliseconds:F0}ms");
                }
                else
                {
                    PluginLog.Verbose(() => $"[Events] WebSocket not open (State: {this._ws?.State}), skipping close");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[Events] Error during SafeCloseAsync");
            }
            finally
            {
                this._ws?.Dispose();
                this._ws = null;
                PluginLog.Info("[Events] Event listener disposed and reset");
            }
        }

        public void Dispose()
        {
            PluginLog.Info("[Events] Dispose - Cleaning up event listener");

            try
            {
                this._cts?.Cancel();
                this._ws?.Dispose();
                this._ws = null;
                PluginLog.Info("[Events] Dispose completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[Events] Error during Dispose");
            }
        }

        // --- helpers ---
        private static Uri BuildWebSocketUri(String baseUrl)
        {
            var uri = new Uri(baseUrl.TrimEnd('/'));
            var builder = new UriBuilder(uri)
            {
                Scheme = (uri.Scheme == "https") ? "wss" : "ws",
                Path = String.Join("/", uri.AbsolutePath.TrimEnd('/'), "api", "websocket")
            };
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
                result = await this._ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Server closed");
                }

                if (buffer.Array != null)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer.Array, 0, result.Count));
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
    }
}
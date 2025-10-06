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
        private Int32 _nextId = 1;

        public event Action<String, Int32?> BrightnessChanged; // (entityId, brightness 0..255 or null)
        public event Action<String, Int32?, Int32?, Int32?, Int32?> ColorTempChanged;
        // args: (entityId, mired, kelvin, min_mireds, max_mireds)



        public event Action<String, Double?, Double?> HsColorChanged; // (entityId, hue 0..360, sat 0..100)
        public event Action<String, Int32?, Int32?, Int32?> RgbColorChanged;
        public event Action<String, Double?, Double?, Int32?> XyColorChanged;

        public event Action<String, Boolean> ScriptRunningChanged; // (entityId, isRunning)

        // Cover events
        public event Action<String, Int32?> CoverPositionChanged; // (entityId, position 0-100)
        public event Action<String, Int32?> CoverTiltChanged;     // (entityId, tilt 0-100)
        public event Action<String, String> CoverStateChanged;    // (entityId, state: "open"/"closed"/"opening"/"closing")



        public async Task<Boolean> ConnectAndSubscribeAsync(String baseUrl, String accessToken, CancellationToken ct)
        {
            try
            {
                var wsUri = BuildWebSocketUri(baseUrl);
                this._ws?.Dispose();
                this._ws = new ClientWebSocket();

                await this._ws.ConnectAsync(wsUri, ct);

                // 1) auth_required
                var first = await this.ReceiveTextAsync(ct);
                var type = ReadType(first);
                if (!String.Equals(type, "auth_required", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 2) auth
                var auth = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
                await this.SendTextAsync(auth, ct);

                // 3) auth_ok
                var authReply = await this.ReceiveTextAsync(ct);
                var authType = ReadType(authReply);
                if (!String.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 4) subscribe_events: state_changed
                var id = Interlocked.Increment(ref this._nextId);
                var sub = JsonSerializer.Serialize(new { id, type = "subscribe_events", event_type = "state_changed" });
                await this.SendTextAsync(sub, ct);

                // 5) expect result success for subscription
                var subReply = await this.ReceiveTextAsync(ct);
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

                this._cts?.Cancel();
                this._cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                this._loop = Task.Run(() => this.ReceiveLoopAsync(this._cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[events] connect/subscribe failed");
                await this.SafeCloseAsync();
                return false;
            }
        }


        // Local helper to compute Kelvin when HA only gives mireds
        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            // Local helper to compute Kelvin when HA only gives mireds
            static Int32 MiredToKelvinSafe(Int32 m) => (Int32)Math.Round(1_000_000.0 / Math.Max(1, m));

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
                        PluginLog.Verbose($"[ReceiveLoopAsync]{entityId} frame received");
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

                    // Cover state
                    Int32? coverPosition = null;
                    Int32? coverTilt = null;
                    String coverState = null;

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
                                {
                                    hue = hs[0].GetDouble(); // 0..360
                                }

                                if (hs[1].ValueKind == JsonValueKind.Number)
                                {
                                    sat = hs[1].GetDouble(); // 0..100
                                }
                            }

                            // NEW: RGB color
                            if (attrs.TryGetProperty("rgb_color", out var rgb) &&
                                rgb.ValueKind == JsonValueKind.Array && rgb.GetArrayLength() >= 3 &&
                                rgb[0].ValueKind == JsonValueKind.Number &&
                                rgb[1].ValueKind == JsonValueKind.Number &&
                                rgb[2].ValueKind == JsonValueKind.Number)
                            {
                                rgbR = rgb[0].GetInt32();
                                rgbG = rgb[1].GetInt32();
                                rgbB = rgb[2].GetInt32();
                            }

                            // NEW: XY color
                            if (attrs.TryGetProperty("xy_color", out var xy) &&
                                xy.ValueKind == JsonValueKind.Array && xy.GetArrayLength() >= 2 &&
                                xy[0].ValueKind == JsonValueKind.Number &&
                                xy[1].ValueKind == JsonValueKind.Number)
                            {
                                xyX = xy[0].GetDouble();
                                xyY = xy[1].GetDouble();
                                // brightness is taken from 'bri' above if present; if not present, handler can fallback
                            }

                            // Cover position (0-100%)
                            if (attrs.TryGetProperty("current_position", out var pos) && pos.ValueKind == JsonValueKind.Number)
                            {
                                coverPosition = HSBHelper.Clamp(pos.GetInt32(), 0, 100);
                            }

                            // Cover tilt position (0-100%)
                            if (attrs.TryGetProperty("current_tilt_position", out var tilt) && tilt.ValueKind == JsonValueKind.Number)
                            {
                                coverTilt = HSBHelper.Clamp(tilt.GetInt32(), 0, 100);
                            }
                        }

                        // Cover state from main state field
                        if (ns.TryGetProperty("state", out var coverSt) && coverSt.ValueKind == JsonValueKind.String)
                        {
                            var stateStr = coverSt.GetString();
                            if (entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase))
                            {
                                coverState = stateStr;
                            }
                        }

                        if (bri.HasValue)
                        {
                            PluginLog.Verbose($" bri={bri} eid={entityId}");
                        }

                        if (hue.HasValue || sat.HasValue)
                        {
                            PluginLog.Verbose($"hs=[{hue?.ToString("F1") ?? "-"},{sat?.ToString("F1") ?? "-"}] eid={entityId}");
                        }

                        if (rgbR.HasValue)
                        {
                            PluginLog.Verbose($"rgb=[{rgbR},{rgbG},{rgbB}] eid={entityId}");
                        }

                        if (xyX.HasValue)
                        {
                            PluginLog.Verbose($" xy=[{xyX:F4},{xyY:F4}] eid={entityId}");
                        }

                        if (ctMired.HasValue || ctKelvin.HasValue)
                        {
                            PluginLog.Verbose($"ct={ctMired}mired/{ctKelvin}K min={minMireds} max={maxMireds} eid={entityId}");
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
                    {
                        ctKelvin = MiredToKelvinSafe(ctMired.Value);
                    }

                    if (!ctMired.HasValue && ctKelvin.HasValue && ctKelvin.Value > 0)
                    {
                        ctMired = (Int32)Math.Round(1_000_000.0 / ctKelvin.Value);
                    }

                    var hasHs = hue.HasValue && sat.HasValue;

                    // Fire events (individually guarded so one can't break the other)
                    try
                    {
                        var briSubs = BrightnessChanged?.GetInvocationList()?.Length ?? 0;
                        PluginLog.Verbose($"[EV] BrightnessChanged subscribers={briSubs} eid={entityId}");
                        BrightnessChanged?.Invoke(entityId, bri);
                        PluginLog.Verbose($"firing brightness event for {entityId} bri={bri}");
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

                    // Cover events
                    if (entityId.StartsWith("cover.", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (coverPosition.HasValue)
                            {
                                PluginLog.Verbose($"[cover] position={coverPosition}% eid={entityId}");
                                CoverPositionChanged?.Invoke(entityId, coverPosition);
                            }
                        }
                        catch { /* keep loop alive */ }

                        try
                        {
                            if (coverTilt.HasValue)
                            {
                                PluginLog.Verbose($"[cover] tilt={coverTilt}% eid={entityId}");
                                CoverTiltChanged?.Invoke(entityId, coverTilt);
                            }
                        }
                        catch { /* keep loop alive */ }

                        try
                        {
                            if (!String.IsNullOrEmpty(coverState))
                            {
                                PluginLog.Verbose($"[cover] state={coverState} eid={entityId}");
                                CoverStateChanged?.Invoke(entityId, coverState);
                            }
                        }
                        catch { /* keep loop alive */ }
                    }
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
                this._cts?.Cancel();
                if (this._ws?.State == WebSocketState.Open)
                {
                    await this._ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None);
                }
            }
            catch { }
            finally
            {
                this._ws?.Dispose();
                this._ws = null;
            }
        }

        public void Dispose() => _ = this.SafeCloseAsync();

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
            var buffer = new ArraySegment<Byte>(new Byte[8192]);
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await this._ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("Server closed");
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
    }
}
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class LightControlService : IDisposable
    {
        private readonly IHaClient _ha;
        private readonly Int32 _brightnessDebounceMs;
        private readonly Int32 _hsDebounceMs;
        private readonly Int32 _tempDebounceMs;

        private readonly DebouncedSender<String, Int32> _brightnessSender;
        private readonly DebouncedSender<String, Hs> _hsSender;
        private readonly DebouncedSender<String, Int32> _tempSender;

        // (Optional) last-sent caches if you need them externally later
        private readonly Object _gate = new();
        private readonly Dictionary<String, Int32> _lastBri = new(StringComparer.OrdinalIgnoreCase);

        public LightControlService(IHaClient ha,
            Int32 brightnessDebounceMs, Int32 hsDebounceMs, Int32 tempDebounceMs)
        {
            PluginLog.Info($"[LightControlService] Constructor - Initializing with debounce timings: brightness={brightnessDebounceMs}ms, hs={hsDebounceMs}ms, temp={tempDebounceMs}ms");
            
            this._ha = ha ?? throw new ArgumentNullException(nameof(ha));
            this._brightnessDebounceMs = brightnessDebounceMs;
            this._hsDebounceMs = hsDebounceMs;
            this._tempDebounceMs = tempDebounceMs;

            try
            {
                this._brightnessSender = new DebouncedSender<String, Int32>(this._brightnessDebounceMs, this.SendBrightnessAsync);
                this._hsSender = new DebouncedSender<String, Hs>(this._hsDebounceMs, this.SendHsAsync);
                this._tempSender = new DebouncedSender<String, Int32>(this._tempDebounceMs, this.SendTempAsync);
                
                PluginLog.Info("[LightControlService] Constructor completed - All debounced senders initialized");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[LightControlService] Constructor failed - Could not initialize debounced senders");
                throw;
            }
        }

        public void SetBrightness(String entityId, Int32 value)
        {
            var clampedValue = HSBHelper.Clamp(value, 0, 255);
            PluginLog.Verbose($"[LightControlService] SetBrightness - entity: {entityId}, value: {value} -> {clampedValue}");
            this._brightnessSender.Set(entityId, clampedValue);
        }

        public void SetHueSat(String entityId, Double h, Double s)
        {
            var wrappedH = HSBHelper.Wrap360(h);
            var clampedS = HSBHelper.Clamp(s, 0, 100);
            PluginLog.Verbose($"[LightControlService] SetHueSat - entity: {entityId}, h: {h:F1} -> {wrappedH:F1}, s: {s:F1} -> {clampedS:F1}");
            this._hsSender.Set(entityId, new Hs(wrappedH, clampedS));
        }

        public void SetTempMired(String entityId, Int32 mired)
        {
            var clampedMired = Math.Max(1, mired);
            PluginLog.Verbose($"[LightControlService] SetTempMired - entity: {entityId}, mired: {mired} -> {clampedMired}");
            this._tempSender.Set(entityId, clampedMired);
        }

        public void CancelPending(String entityId)
        {
            PluginLog.Verbose($"[LightControlService] CancelPending - entity: {entityId}, canceling all pending operations");
            try
            {
                this._brightnessSender.Cancel(entityId);
                this._hsSender.Cancel(entityId);
                this._tempSender.Cancel(entityId);
                PluginLog.Verbose($"[LightControlService] CancelPending completed for {entityId}");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[LightControlService] CancelPending failed for {entityId}");
            }
        }

        public async Task<Boolean> TurnOnAsync(String entityId, JsonElement? data = null, CancellationToken ct = default)
        {
            var hasData = data.HasValue;
            PluginLog.Info($"[LightControlService] TurnOnAsync - entity: {entityId}, hasData: {hasData}");
            
            try
            {
                var startTime = DateTime.UtcNow;
                var (ok, error) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, ct).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - startTime;
                
                if (ok)
                {
                    PluginLog.Info($"[LightControlService] TurnOnAsync SUCCESS - {entityId} turned on in {elapsed.TotalMilliseconds:F0}ms");
                }
                else
                {
                    PluginLog.Warning($"[LightControlService] TurnOnAsync FAILED - {entityId} failed in {elapsed.TotalMilliseconds:F0}ms: {error}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[LightControlService] TurnOnAsync EXCEPTION - {entityId}");
                return false;
            }
        }

        public async Task<Boolean> TurnOffAsync(String entityId, CancellationToken ct = default)
        {
            PluginLog.Info($"[LightControlService] TurnOffAsync - entity: {entityId}");
            
            try
            {
                var startTime = DateTime.UtcNow;
                var (ok, error) = await this._ha.CallServiceAsync("light", "turn_off", entityId, null, ct).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - startTime;
                
                if (ok)
                {
                    PluginLog.Info($"[LightControlService] TurnOffAsync SUCCESS - {entityId} turned off in {elapsed.TotalMilliseconds:F0}ms");
                }
                else
                {
                    PluginLog.Warning($"[LightControlService] TurnOffAsync FAILED - {entityId} failed in {elapsed.TotalMilliseconds:F0}ms: {error}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[LightControlService] TurnOffAsync EXCEPTION - {entityId}");
                return false;
            }
        }

        public async Task<Boolean> ToggleAsync(String entityId, CancellationToken ct = default)
        {
            PluginLog.Info($"[LightControlService] ToggleAsync - entity: {entityId}");
            
            try
            {
                var startTime = DateTime.UtcNow;
                var (ok, error) = await this._ha.CallServiceAsync("light", "toggle", entityId, null, ct).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - startTime;
                
                if (ok)
                {
                    PluginLog.Info($"[LightControlService] ToggleAsync SUCCESS - {entityId} toggled in {elapsed.TotalMilliseconds:F0}ms");
                }
                else
                {
                    PluginLog.Warning($"[LightControlService] ToggleAsync FAILED - {entityId} failed in {elapsed.TotalMilliseconds:F0}ms: {error}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"[LightControlService] ToggleAsync EXCEPTION - {entityId}");
                return false;
            }
        }

        private async Task SendBrightnessAsync(String entityId, Int32 target)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var data = JsonSerializer.SerializeToElement(new { brightness = target });
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    lock (this._gate)
                    {
                        this._lastBri[entityId] = target;
                    }

                    PluginLog.Info($"[light] bri={target} -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] bri send failed: {err}");
                    HealthBus.Error(err ?? "Brightness change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[light] SendBrightnessAsync exception");
            }
        }

        private async Task SendHsAsync(String entityId, Hs hs)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

                var data = JsonSerializer.SerializeToElement(new { hs_color = new Object[] { hs.H, hs.S } });
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    PluginLog.Info($"[light] hs=[{hs.H:F0},{hs.S:F0}] -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] hs send failed: {err}");
                    HealthBus.Error(err ?? "Color change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[light] SendHsAsync exception");
            }
        }

        private async Task SendTempAsync(String entityId, Int32 mired)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

                var kelvin = ColorTemp.MiredToKelvin(mired);
                var data = JsonSerializer.SerializeToElement(new { color_temp_kelvin = kelvin });

                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    PluginLog.Info($"[light] temp={kelvin}K ({mired} mired) -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] temp send failed: {err}");
                    HealthBus.Error(err ?? "Temp change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[light] SendTempAsync exception");
            }
        }

        public void Dispose()
        {
            PluginLog.Info("[LightControlService] Dispose - Cleaning up debounced senders");
            
            try
            {
                this._brightnessSender?.Dispose();
                this._hsSender?.Dispose();
                this._tempSender?.Dispose();
                
                PluginLog.Info("[LightControlService] Dispose completed successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[LightControlService] Dispose encountered errors during cleanup");
            }
        }
    }
}
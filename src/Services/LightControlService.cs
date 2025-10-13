namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck.HomeAssistantPlugin.Services;

    internal sealed class LightControlService : ILightControlService
    {
        // ====================================================================
        // CONSTANTS - Light Control Service Configuration
        // ====================================================================

        // --- Brightness Range Constants ---
        private const Int32 MinBrightnessValue = 0;                    // Minimum brightness value (off)
        private const Int32 MaxBrightnessValue = 255;                  // Maximum brightness value (full brightness)

        // --- Saturation Range Constants ---
        private const Double MinSaturationValue = 0.0;                 // Minimum saturation value (grayscale)
        private const Double MaxSaturationValue = 100.0;               // Maximum saturation value (fully saturated)

        // --- Color Temperature Constants ---
        private const Int32 MinMiredValue = 1;                         // Minimum mired value to prevent division by zero

        // --- Service Timeout Constants ---
        private const Int32 ServiceCallTimeoutSeconds = 4;             // Timeout for Home Assistant service calls

        private readonly IHaClient _ha;
        private readonly Int32 _brightnessDebounceMs;
        private readonly Int32 _hsDebounceMs;
        private readonly Int32 _tempDebounceMs;

        private readonly DebouncedSender<String, Int32> _brightnessSender;
        private readonly DebouncedSender<String, HueSaturation> _hsSender;
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
                this._hsSender = new DebouncedSender<String, HueSaturation>(this._hsDebounceMs, this.SendHsAsync);
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
            var clampedValue = HSBHelper.Clamp(value, MinBrightnessValue, MaxBrightnessValue);
            PluginLog.Verbose(() => $"[LightControlService] SetBrightness - entity: {entityId}, value: {value} -> {clampedValue}");
            this._brightnessSender.Set(entityId, clampedValue);
        }

        public void SetHueSat(String entityId, Double h, Double s)
        {
            var wrappedH = HSBHelper.Wrap360(h);
            var clampedS = HSBHelper.Clamp(s, MinSaturationValue, MaxSaturationValue);
            PluginLog.Verbose(() => $"[LightControlService] SetHueSat - entity: {entityId}, h: {h:F1} -> {wrappedH:F1}, s: {s:F1} -> {clampedS:F1}");
            this._hsSender.Set(entityId, new HueSaturation(wrappedH, clampedS));
        }

        public void SetTempMired(String entityId, Int32 mired)
        {
            var clampedMired = Math.Max(MinMiredValue, mired);
            PluginLog.Verbose(() => $"[LightControlService] SetTempMired - entity: {entityId}, mired: {mired} -> {clampedMired}");
            this._tempSender.Set(entityId, clampedMired);
        }

        public void CancelPending(String entityId)
        {
            PluginLog.Verbose(() => $"[LightControlService] CancelPending - entity: {entityId}, canceling all pending operations");
            try
            {
                this._brightnessSender.Cancel(entityId);
                this._hsSender.Cancel(entityId);
                this._tempSender.Cancel(entityId);
                PluginLog.Verbose(() => $"[LightControlService] CancelPending completed for {entityId}");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[LightControlService] CancelPending failed for {entityId}");
            }
        }

        public async Task<Boolean> TurnOnAsync(String entityId, JsonElement? data = null, CancellationToken ct = default)
        {
            try
            {
                var dataStr = data?.ToString() ?? "null";
                PluginLog.Info($"[light] Sending turn_on command to {entityId} with data: {dataStr}");
                
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, ct).ConfigureAwait(false);
                
                if (ok)
                {
                    PluginLog.Info($"[light] turn_on -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] turn_on failed for {entityId}: {err}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] TurnOnAsync exception for {entityId}");
                return false;
            }
        }

        public async Task<Boolean> TurnOffAsync(String entityId, CancellationToken ct = default)
        {
            try
            {
                PluginLog.Info($"[light] Sending turn_off command to {entityId}");
                
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_off", entityId, null, ct).ConfigureAwait(false);
                
                if (ok)
                {
                    PluginLog.Info($"[light] turn_off -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] turn_off failed for {entityId}: {err}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] TurnOffAsync exception for {entityId}");
                return false;
            }
        }

        public async Task<Boolean> ToggleAsync(String entityId, CancellationToken ct = default)
        {
            try
            {
                PluginLog.Info($"[light] Sending toggle command to {entityId}");
                
                var (ok, err) = await this._ha.CallServiceAsync("light", "toggle", entityId, null, ct).ConfigureAwait(false);
                
                if (ok)
                {
                    PluginLog.Info($"[light] toggle -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] toggle failed for {entityId}: {err}");
                }
                
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] ToggleAsync exception for {entityId}");
                return false;
            }
        }

        private async Task SendBrightnessAsync(String entityId, Int32 target)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ServiceCallTimeoutSeconds));
                var data = JsonSerializer.SerializeToElement(new { brightness = target });
                
                PluginLog.Info($"[light] Sending brightness command to {entityId} with data: {data}");
                
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    lock (this._gate)
                    {
                        this._lastBri[entityId] = target;
                    }

                    PluginLog.Info(() => $"[light] bri={target} -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] bri send failed: {err}");
                    HealthBus.Error(err ?? "Brightness change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] SendBrightnessAsync exception for {entityId}");
            }
        }

        private async Task SendHsAsync(String entityId, HueSaturation hs)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ServiceCallTimeoutSeconds));

                var data = JsonSerializer.SerializeToElement(new { hs_color = new Object[] { hs.H, hs.S } });
                
                PluginLog.Info($"[light] Sending hue/saturation command to {entityId} with data: {data}");
                
                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    PluginLog.Info(() => $"[light] hs=[{hs.H:F0},{hs.S:F0}] -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] hs send failed: {err}");
                    HealthBus.Error(err ?? "Color change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] SendHsAsync exception for {entityId}");
            }
        }

        private async Task SendTempAsync(String entityId, Int32 mired)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ServiceCallTimeoutSeconds));

                var kelvin = ColorTemp.MiredToKelvin(mired);
                var data = JsonSerializer.SerializeToElement(new { color_temp_kelvin = kelvin });

                PluginLog.Info($"[light] Sending temperature command to {entityId} with data: {data}");

                var (ok, err) = await this._ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    PluginLog.Info(() => $"[light] temp={kelvin}K ({mired} mired) -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[light] temp send failed: {err}");
                    HealthBus.Error(err ?? "Temp change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"[light] SendTempAsync exception for {entityId}");
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
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
        private readonly DebouncedSender<String, Hs>  _hsSender;
        private readonly DebouncedSender<String, Int32> _tempSender;

        // (Optional) last-sent caches if you need them externally later
        private readonly Object _gate = new();
        private readonly Dictionary<String, Int32> _lastBri = new(StringComparer.OrdinalIgnoreCase);

        public LightControlService(IHaClient ha,
            Int32 brightnessDebounceMs, Int32 hsDebounceMs, Int32 tempDebounceMs)
        {
            this._ha = ha ?? throw new ArgumentNullException(nameof(ha));
            this._brightnessDebounceMs = brightnessDebounceMs;
            this._hsDebounceMs         = hsDebounceMs;
            this._tempDebounceMs       = tempDebounceMs;

            this._brightnessSender = new DebouncedSender<String, Int32>(this._brightnessDebounceMs, this.SendBrightnessAsync);
            this._hsSender         = new DebouncedSender<String, Hs>(this._hsDebounceMs, this.SendHsAsync);
            this._tempSender       = new DebouncedSender<String, Int32>(this._tempDebounceMs, this.SendTempAsync);
        }

        public void SetBrightness(String entityId, Int32 value)
            => this._brightnessSender.Set(entityId, HSBHelper.Clamp(value, 0, 255));

        public void SetHueSat(String entityId, Double h, Double s)
            => this._hsSender.Set(entityId, new Hs(HSBHelper.Wrap360(h), HSBHelper.Clamp(s, 0, 100)));

        public void SetTempMired(String entityId, Int32 mired)
            => this._tempSender.Set(entityId, Math.Max(1, mired));

        public void CancelPending(String entityId)
        {
            this._brightnessSender.Cancel(entityId);
            this._hsSender.Cancel(entityId);
            this._tempSender.Cancel(entityId);
        }

        public async Task<Boolean> TurnOnAsync(String entityId, JsonElement? data = null, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("light", "turn_on", entityId, data, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> TurnOffAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("light", "turn_off", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> ToggleAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("light", "toggle", entityId, null, ct).ConfigureAwait(false)).ok;

        private async Task SendBrightnessAsync(String entityId, Int32 target)
        {
            try
            {
                if (!this._ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
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
                if (!this._ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
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
                if (!this._ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
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
            this._brightnessSender?.Dispose();
            this._hsSender?.Dispose();
            this._tempSender?.Dispose();
        }
    }
}

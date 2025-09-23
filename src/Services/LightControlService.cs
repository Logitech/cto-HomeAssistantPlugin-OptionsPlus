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
        private readonly int _brightnessDebounceMs;
        private readonly int _hsDebounceMs;
        private readonly int _tempDebounceMs;

        private readonly DebouncedSender<string, int> _brightnessSender;
        private readonly DebouncedSender<string, Hs>  _hsSender;
        private readonly DebouncedSender<string, int> _tempSender;

        // (Optional) last-sent caches if you need them externally later
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _lastBri = new(StringComparer.OrdinalIgnoreCase);

        public LightControlService(IHaClient ha,
            int brightnessDebounceMs, int hsDebounceMs, int tempDebounceMs)
        {
            _ha = ha ?? throw new ArgumentNullException(nameof(ha));
            _brightnessDebounceMs = brightnessDebounceMs;
            _hsDebounceMs         = hsDebounceMs;
            _tempDebounceMs       = tempDebounceMs;

            _brightnessSender = new DebouncedSender<string, int>(_brightnessDebounceMs, SendBrightnessAsync);
            _hsSender         = new DebouncedSender<string, Hs>(_hsDebounceMs, SendHsAsync);
            _tempSender       = new DebouncedSender<string, int>(_tempDebounceMs, SendTempAsync);
        }

        public void SetBrightness(string entityId, int value)
            => _brightnessSender.Set(entityId, HSBHelper.Clamp(value, 0, 255));

        public void SetHueSat(string entityId, double h, double s)
            => _hsSender.Set(entityId, new Hs(HSBHelper.Wrap360(h), HSBHelper.Clamp(s, 0, 100)));

        public void SetTempMired(string entityId, int mired)
            => _tempSender.Set(entityId, Math.Max(1, mired));

        public void CancelPending(string entityId)
        {
            _brightnessSender.Cancel(entityId);
            _hsSender.Cancel(entityId);
            _tempSender.Cancel(entityId);
        }

        public async Task<bool> TurnOnAsync(string entityId, JsonElement? data = null, CancellationToken ct = default)
            => (await _ha.CallServiceAsync("light", "turn_on", entityId, data, ct).ConfigureAwait(false)).ok;

        public async Task<bool> TurnOffAsync(string entityId, CancellationToken ct = default)
            => (await _ha.CallServiceAsync("light", "turn_off", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<bool> ToggleAsync(string entityId, CancellationToken ct = default)
            => (await _ha.CallServiceAsync("light", "toggle", entityId, null, ct).ConfigureAwait(false)).ok;

        private async Task SendBrightnessAsync(string entityId, int target)
        {
            try
            {
                if (!_ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var data = JsonSerializer.SerializeToElement(new { brightness = target });
                var (ok, err) = await _ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
                if (ok)
                {
                    lock (_gate) _lastBri[entityId] = target;
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

        private async Task SendHsAsync(string entityId, Hs hs)
        {
            try
            {
                if (!_ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

                var data = JsonSerializer.SerializeToElement(new { hs_color = new object[] { hs.H, hs.S } });
                var (ok, err) = await _ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
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

        private async Task SendTempAsync(string entityId, int mired)
        {
            try
            {
                if (!_ha.IsAuthenticated) { HealthBus.Error("Connection lost"); return; }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

                var kelvin = ColorTemp.MiredToKelvin(mired);
                var data = JsonSerializer.SerializeToElement(new { color_temp_kelvin = kelvin });

                var (ok, err) = await _ha.CallServiceAsync("light", "turn_on", entityId, data, cts.Token).ConfigureAwait(false);
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
            _brightnessSender?.Dispose();
            _hsSender?.Dispose();
            _tempSender?.Dispose();
        }
    }
}

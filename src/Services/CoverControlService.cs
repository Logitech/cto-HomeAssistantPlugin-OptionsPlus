namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class CoverControlService : IDisposable
    {
        private readonly IHaClient _ha;
        private readonly Int32 _positionDebounceMs;
        private readonly Int32 _tiltDebounceMs;

        private readonly DebouncedSender<String, Int32> _positionSender;
        private readonly DebouncedSender<String, Int32> _tiltSender;

        // Optional: last-sent caches if needed externally later
        private readonly Object _gate = new();
        private readonly Dictionary<String, Int32> _lastPosition = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<String, Int32> _lastTilt = new(StringComparer.OrdinalIgnoreCase);

        public CoverControlService(IHaClient ha, Int32 positionDebounceMs, Int32 tiltDebounceMs)
        {
            this._ha = ha ?? throw new ArgumentNullException(nameof(ha));
            this._positionDebounceMs = positionDebounceMs;
            this._tiltDebounceMs = tiltDebounceMs;

            this._positionSender = new DebouncedSender<String, Int32>(this._positionDebounceMs, this.SendPositionAsync);
            this._tiltSender = new DebouncedSender<String, Int32>(this._tiltDebounceMs, this.SendTiltAsync);
        }

        public void SetPosition(String entityId, Int32 position)
            => this._positionSender.Set(entityId, HSBHelper.Clamp(position, 0, 100));

        public void SetTilt(String entityId, Int32 tilt)
            => this._tiltSender.Set(entityId, HSBHelper.Clamp(tilt, 0, 100));

        public void CancelPending(String entityId)
        {
            this._positionSender.Cancel(entityId);
            this._tiltSender.Cancel(entityId);
        }

        public async Task<Boolean> OpenAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "open_cover", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> CloseAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "close_cover", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> StopAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "stop_cover", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> ToggleAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "toggle", entityId, null, ct).ConfigureAwait(false)).ok;

        // Tilt controls for venetian blinds
        public async Task<Boolean> OpenTiltAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "open_cover_tilt", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> CloseTiltAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "close_cover_tilt", entityId, null, ct).ConfigureAwait(false)).ok;

        public async Task<Boolean> StopTiltAsync(String entityId, CancellationToken ct = default)
            => (await this._ha.CallServiceAsync("cover", "stop_cover_tilt", entityId, null, ct).ConfigureAwait(false)).ok;

        private async Task SendPositionAsync(String entityId, Int32 targetPosition)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var data = JsonSerializer.SerializeToElement(new { position = targetPosition });
                var (ok, err) = await this._ha.CallServiceAsync("cover", "set_cover_position", entityId, data, cts.Token).ConfigureAwait(false);
                
                if (ok)
                {
                    lock (this._gate)
                    {
                        this._lastPosition[entityId] = targetPosition;
                    }
                    PluginLog.Info($"[cover] position={targetPosition}% -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[cover] position send failed: {err}");
                    HealthBus.Error(err ?? "Position change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[cover] SendPositionAsync exception");
            }
        }

        private async Task SendTiltAsync(String entityId, Int32 targetTilt)
        {
            try
            {
                if (!this._ha.IsAuthenticated)
                { HealthBus.Error("Connection lost"); return; }
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var data = JsonSerializer.SerializeToElement(new { tilt_position = targetTilt });
                var (ok, err) = await this._ha.CallServiceAsync("cover", "set_cover_tilt_position", entityId, data, cts.Token).ConfigureAwait(false);
                
                if (ok)
                {
                    lock (this._gate)
                    {
                        this._lastTilt[entityId] = targetTilt;
                    }
                    PluginLog.Info($"[cover] tilt={targetTilt}% -> {entityId} OK");
                }
                else
                {
                    PluginLog.Warning($"[cover] tilt send failed: {err}");
                    HealthBus.Error(err ?? "Tilt change failed");
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "[cover] SendTiltAsync exception");
            }
        }

        public void Dispose()
        {
            this._positionSender?.Dispose();
            this._tiltSender?.Dispose();
        }
    }
}
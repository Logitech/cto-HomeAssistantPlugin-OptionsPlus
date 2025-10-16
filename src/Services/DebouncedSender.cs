namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Timers;

    internal sealed class DebouncedSender<TKey, TValue> : IDisposable
        where TKey : notnull
    {
        private readonly Object _gate = new();
        private readonly Dictionary<TKey, (TValue value, Timer timer)> _map = new();
        private readonly Int32 _delayMs;
        private readonly Func<TKey, TValue, Task> _send;

        public DebouncedSender(Int32 delayMs, Func<TKey, TValue, Task> send)
        {
            PluginLog.Verbose($"[DebouncedSender] Constructor - delayMs: {delayMs}");
            this._delayMs = delayMs;
            this._send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public void Set(TKey key, TValue value)
        {
            lock (this._gate)
            {
                var isNewEntry = !this._map.TryGetValue(key, out var entry);
                
                if (isNewEntry)
                {
                    PluginLog.Verbose($"[DebouncedSender] Set NEW - key: {key}, value: {value}, delay: {this._delayMs}ms");
                    var timer = new Timer { AutoReset = false, Interval = this._delayMs };
                    timer.Elapsed += async (_, __) => await this.FireAsync(key).ConfigureAwait(false);
                    this._map[key] = (value, timer);
                }
                else
                {
                    PluginLog.Verbose($"[DebouncedSender] Set UPDATE - key: {key}, oldValue: {entry.value}, newValue: {value}, restarting timer");
                    entry.value = value;
                    entry.timer.Stop();
                    this._map[key] = entry;
                }
                this._map[key].timer.Start();
            }
        }

        public void Cancel(TKey key)
        {
            lock (this._gate)
            {
                if (this._map.TryGetValue(key, out var entry))
                {
                    PluginLog.Verbose($"[DebouncedSender] Cancel - key: {key}, stopping timer");
                    entry.timer.Stop();
                    this._map[key] = entry;
                }
                else
                {
                    PluginLog.Verbose($"[DebouncedSender] Cancel - key: {key}, no entry found");
                }
            }
        }

        private async Task FireAsync(TKey key)
        {
            TValue value;
            lock (this._gate)
            {
                if (!this._map.TryGetValue(key, out var entry))
                {
                    PluginLog.Verbose($"[DebouncedSender] FireAsync - key: {key}, entry not found (already processed?)");
                    return;
                }

                value = entry.value;
            }

            PluginLog.Verbose($"[DebouncedSender] FireAsync START - key: {key}, value: {value}");
            var startTime = DateTime.UtcNow;

            try
            {
                await this._send(key, value).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - startTime;
                PluginLog.Verbose($"[DebouncedSender] FireAsync SUCCESS - key: {key}, completed in {elapsed.TotalMilliseconds:F0}ms");
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                // Log the exception instead of silently swallowing it
                // Keep UI responsive by not rethrowing, but ensure visibility of errors
                PluginLog.Warning(ex, $"[DebouncedSender] FireAsync FAILED - key: {key}, failed after {elapsed.TotalMilliseconds:F0}ms");
            }
        }

        public void Dispose()
        {
            PluginLog.Verbose($"[DebouncedSender] Dispose - Cleaning up {this._map.Count} pending entries");
            
            lock (this._gate)
            {
                try
                {
                    foreach (var entry in this._map.Values)
                    {
                        entry.timer.Dispose();
                    }

                    var count = this._map.Count;
                    this._map.Clear();
                    
                    PluginLog.Verbose($"[DebouncedSender] Dispose completed - Disposed {count} entries");
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "[DebouncedSender] Dispose encountered errors during cleanup");
                }
            }
        }
    }
}
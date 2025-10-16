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
            this._delayMs = delayMs;
            this._send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public void Set(TKey key, TValue value)
        {
            lock (this._gate)
            {
                if (!this._map.TryGetValue(key, out var entry))
                {
                    var timer = new Timer { AutoReset = false, Interval = this._delayMs };
                    timer.Elapsed += async (_, __) => await this.FireAsync(key).ConfigureAwait(false);
                    this._map[key] = (value, timer);
                }
                else
                {
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
                    entry.timer.Stop();
                    this._map[key] = entry;
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
                    return;
                }

                value = entry.value;
            }

            try
            {
                await this._send(key, value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log the exception instead of silently swallowing it
                // Keep UI responsive by not rethrowing, but ensure visibility of errors
                PluginLog.Warning(ex, $"DebouncedSender failed to send for key: {key}");
            }
        }

        public void Dispose()
        {
            lock (this._gate)
            {
                foreach (var entry in this._map.Values)
                {
                    entry.timer.Dispose();
                }

                this._map.Clear();
            }
        }
    }
}
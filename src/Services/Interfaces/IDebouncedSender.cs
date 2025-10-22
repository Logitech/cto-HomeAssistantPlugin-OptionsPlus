namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;

    /// <summary>
    /// Generic debouncing contract that coalesces rapid <typeparamref name="TValue"/> updates
    /// keyed by <typeparamref name="TKey"/> and invokes a single send after a quiet period.
    /// Implementations should be thread-safe for callers that invoke <see cref="Set"/> and
    /// <see cref="Cancel"/> from different threads.
    /// </summary>
    /// <typeparam name="TKey">Key type used to identify independent debounce channels.</typeparam>
    /// <typeparam name="TValue">Value type to be sent after the debounce interval.</typeparam>
    public interface IDebouncedSender<TKey, TValue> : IDisposable
        where TKey : notnull
    {
        /// <summary>
        /// Schedules a debounced send for the specified <paramref name="key"/>.
        /// If called repeatedly within the debounce window, only the latest <paramref name="value"/>
        /// should be sent when the timer elapses.
        /// </summary>
        /// <param name="key">Logical channel identifier.</param>
        /// <param name="value">The latest value to be sent after debounce.</param>
        void Set(TKey key, TValue value);

        /// <summary>
        /// Cancels any pending send for the specified <paramref name="key"/>.
        /// If no entry exists for the key, the call should be a no-op.
        /// </summary>
        /// <param name="key">Logical channel identifier.</param>
        void Cancel(TKey key);
    }
}
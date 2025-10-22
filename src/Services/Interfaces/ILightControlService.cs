namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Service responsible for controlling Home Assistant light entities.
    /// Provides debounced setters for user-driven adjustments and direct service actions
    /// for on/off/toggle operations.
    /// </summary>
    public interface ILightControlService : IDisposable
    {
        /// <summary>
        /// Queues a debounced brightness update for the specified light entity.
        /// Subsequent calls within the debounce window coalesce; only the latest value is sent.
        /// </summary>
        /// <param name="entityId">Target light entity id (e.g., <c>light.kitchen</c>).</param>
        /// <param name="value">Brightness value in HA scale (typically 0–255).</param>
        void SetBrightness(String entityId, Int32 value);

        /// <summary>
        /// Queues a debounced hue/saturation update for the specified light entity.
        /// Hue is wrapped to [0, 360); saturation is clamped to the service’s supported range.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        /// <param name="h">Hue in degrees (0–360).</param>
        /// <param name="s">Saturation percentage (0–100).</param>
        void SetHueSat(String entityId, Double h, Double s);

        /// <summary>
        /// Queues a debounced color-temperature update (in mireds) for the specified entity.
        /// Implementations may convert to Kelvin if required by the backend.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        /// <param name="mired">Color temperature in mireds (≤ 1 mired is clamped to a safe minimum).</param>
        void SetTempMired(String entityId, Int32 mired);

        /// <summary>
        /// Cancels any pending debounced updates (brightness/HS/temp) for the specified entity.
        /// No-op if no pending updates exist.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        void CancelPending(String entityId);

        /// <summary>
        /// Turns the specified light entity on, optionally including a JSON payload
        /// (e.g., brightness, HS color, temperature) in the same call.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        /// <param name="data">Optional JSON payload to include with the service call.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if the service call succeeded; otherwise <c>false</c>.</returns>
        Task<Boolean> TurnOnAsync(String entityId, JsonElement? data = null, CancellationToken ct = default);

        /// <summary>
        /// Turns the specified light entity off.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if the service call succeeded; otherwise <c>false</c>.</returns>
        Task<Boolean> TurnOffAsync(String entityId, CancellationToken ct = default);

        /// <summary>
        /// Toggles the specified light entity on/off.
        /// </summary>
        /// <param name="entityId">Target light entity id.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if the service call succeeded; otherwise <c>false</c>.</returns>
        Task<Boolean> ToggleAsync(String entityId, CancellationToken ct = default);
    }
}
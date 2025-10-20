namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Service responsible for managing light states, HSB values, and color data
    /// </summary>
    public interface ILightStateManager
    {
        /// <summary>
        /// Updates the on/off state and optionally brightness for a light
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="isOn">Whether the light is on</param>
        /// <param name="brightness">Optional brightness value (0-255)</param>
        void UpdateLightState(String entityId, Boolean isOn, Int32? brightness = null);

        /// <summary>
        /// Updates the hue and saturation values for a light
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="hue">Hue value in degrees (0-360)</param>
        /// <param name="saturation">Saturation value as percentage (0-100)</param>
        void UpdateHsColor(String entityId, Double? hue, Double? saturation);

        /// <summary>
        /// Updates the color temperature for a light
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="mired">Color temperature in mireds</param>
        /// <param name="kelvin">Color temperature in Kelvin</param>
        /// <param name="minM">Minimum mireds supported</param>
        /// <param name="maxM">Maximum mireds supported</param>
        void UpdateColorTemp(String entityId, Int32? mired, Int32? kelvin, Int32? minM, Int32? maxM);

        /// <summary>
        /// Gets the current HSB values for a light
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <returns>Tuple of hue, saturation, and brightness</returns>
        (Double H, Double S, Int32 B) GetHsbValues(String entityId);

        /// <summary>
        /// Gets the effective brightness for display (0 if off, cached brightness if on)
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <returns>Brightness value (0-255)</returns>
        Int32 GetEffectiveBrightness(String entityId);

        /// <summary>
        /// Checks if a light is currently on
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <returns>True if the light is on</returns>
        Boolean IsLightOn(String entityId);

        /// <summary>
        /// Sets the capabilities for a light entity
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="caps">Light capabilities</param>
        void SetCapabilities(String entityId, LightCaps caps);

        /// <summary>
        /// Gets the capabilities for a light entity
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <returns>Light capabilities</returns>
        LightCaps GetCapabilities(String entityId);

        /// <summary>
        /// Gets the color temperature range and current value for a light
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <returns>Tuple of min, max, and current mireds, or null if not supported</returns>
        (Int32 Min, Int32 Max, Int32 Cur)? GetColorTempMired(String entityId);

        /// <summary>
        /// Sets cached brightness for a light entity
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="brightness">Brightness value (0-255)</param>
        void SetCachedBrightness(String entityId, Int32 brightness);

        /// <summary>
        /// Initializes light state from parsed light data
        /// </summary>
        /// <param name="lights">Collection of light data</param>
        void InitializeLightStates(IEnumerable<LightData> lights);

        /// <summary>
        /// Sets cached color temperature for a light entity
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="minM">Optional minimum mireds (preserves existing if null)</param>
        /// <param name="maxM">Optional maximum mireds (preserves existing if null)</param>
        /// <param name="curMired">Current mired value</param>
        void SetCachedTempMired(String entityId, Int32? minM, Int32? maxM, Int32 curMired);

        /// <summary>
        /// Removes an entity from all internal caches
        /// </summary>
        /// <param name="entityId">Entity ID to remove</param>
        void RemoveEntity(String entityId);

        /// <summary>
        /// Gets all currently tracked entity IDs
        /// </summary>
        /// <returns>Collection of entity IDs</returns>
        IEnumerable<String> GetTrackedEntityIds();
    }
}
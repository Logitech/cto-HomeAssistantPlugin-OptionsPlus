namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Loupedeck.HomeAssistantPlugin.Models;

    /// <summary>
    /// Implementation of ILightStateManager for managing light states, HSB values, and color data
    /// </summary>
    internal class LightStateManager : ILightStateManager
    {
        // Constants from the original class
        private const Double DefaultHue = 0;
        private const Double DefaultSaturation = 100;
        private const Double MinSaturation = 0;
        private const Double MaxSaturation = 100;
        private const Int32 BrightnessOff = 0;
        private const Int32 MaxBrightness = 255;
        private const Int32 MidBrightness = 128;
        private const Int32 DefaultMinMireds = 153;
        private const Int32 DefaultMaxMireds = 500;
        private const Int32 DefaultWarmMired = 370;

        // Internal state dictionaries
        private readonly Dictionary<String, (Double H, Double S, Int32 B)> _hsbByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<String, Boolean> _isOnByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<String, LightCaps> _capsByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<String, (Int32 Min, Int32 Max, Int32 Cur)> _tempMiredByEntity =
            new(StringComparer.OrdinalIgnoreCase);

        public void UpdateLightState(String entityId, Boolean isOn, Int32? brightness = null)
        {
            PluginLog.Verbose($"[LightStateManager] UpdateLightState: {entityId} isOn={isOn} brightness={brightness}");

            this._isOnByEntity[entityId] = isOn;

            if (brightness.HasValue)
            {
                this.SetCachedBrightness(entityId, brightness.Value);
            }
        }

        public void UpdateHsColor(String entityId, Double? hue, Double? saturation)
        {
            PluginLog.Verbose($"[LightStateManager] UpdateHsColor: {entityId} hue={hue} saturation={saturation}");

            if (!this._hsbByEntity.TryGetValue(entityId, out var hsb))
            {
                hsb = (DefaultHue, DefaultSaturation, MidBrightness);
            }

            var newH = hue.HasValue ? HSBHelper.Wrap360(hue.Value) : hsb.H;
            var newS = saturation.HasValue ? HSBHelper.Clamp(saturation.Value, MinSaturation, MaxSaturation) : hsb.S;

            this._hsbByEntity[entityId] = (newH, newS, hsb.B);
        }

        public void UpdateColorTemp(String entityId, Int32? mired, Int32? kelvin, Int32? minM, Int32? maxM)
        {
            PluginLog.Verbose($"[LightStateManager] UpdateColorTemp: {entityId} mired={mired} kelvin={kelvin} minM={minM} maxM={maxM}");

            var existing = this._tempMiredByEntity.TryGetValue(entityId, out var temp)
                ? temp
                : (Min: DefaultMinMireds, Max: DefaultMaxMireds, Cur: DefaultWarmMired);

            var min = minM ?? existing.Min;
            var max = maxM ?? existing.Max;
            var cur = existing.Cur;

            if (mired.HasValue)
            {
                cur = HSBHelper.Clamp(mired.Value, min, max);
            }
            else if (kelvin.HasValue)
            {
                cur = HSBHelper.Clamp(ColorTemp.KelvinToMired(kelvin.Value), min, max);
            }

            this._tempMiredByEntity[entityId] = (min, max, cur);
        }

        public (Double H, Double S, Int32 B) GetHsbValues(String entityId)
        {
            return this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? hsb
                : (DefaultHue, MinSaturation, BrightnessOff);
        }

        public Int32 GetEffectiveBrightness(String entityId)
        {
            // If we know it's OFF, show 0; otherwise show cached B
            return this._isOnByEntity.TryGetValue(entityId, out var on) && !on
                ? BrightnessOff
                : this._hsbByEntity.TryGetValue(entityId, out var hsb) ? hsb.B : BrightnessOff;
        }

        public Boolean IsLightOn(String entityId) => this._isOnByEntity.TryGetValue(entityId, out var isOn) && isOn;

        public void SetCapabilities(String entityId, LightCaps caps)
        {
            this._capsByEntity[entityId] = caps;
            PluginLog.Verbose($"[LightStateManager] Set capabilities for {entityId}: onoff={caps.OnOff} bri={caps.Brightness} ctemp={caps.ColorTemp} color={caps.ColorHs}");
        }

        public LightCaps GetCapabilities(String entityId)
        {
            return this._capsByEntity.TryGetValue(entityId, out var caps)
                ? caps
                : new LightCaps(true, false, false, false); // Safe default: on/off only
        }

        public (Int32 Min, Int32 Max, Int32 Cur)? GetColorTempMired(String entityId) => this._tempMiredByEntity.TryGetValue(entityId, out var temp) ? temp : null;

        public void SetCachedBrightness(String entityId, Int32 brightness)
        {
            PluginLog.Verbose($"[LightStateManager] SetCachedBrightness: {entityId} brightness={brightness}");

            var clampedBrightness = HSBHelper.Clamp(brightness, BrightnessOff, MaxBrightness);

            this._hsbByEntity[entityId] = this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? (hsb.H, hsb.S, clampedBrightness)
                : (DefaultHue, MinSaturation, clampedBrightness);
        }

        public void InitializeLightStates(IEnumerable<LightData> lights)
        {
            PluginLog.Info($"[LightStateManager] Initializing light states for {lights.Count()} lights");

            // Clear existing data
            this._hsbByEntity.Clear();
            this._isOnByEntity.Clear();
            this._capsByEntity.Clear();
            this._tempMiredByEntity.Clear();

            foreach (var light in lights)
            {
                // Set on/off state
                this._isOnByEntity[light.EntityId] = light.IsOn;

                // Set HSB values
                this._hsbByEntity[light.EntityId] = (light.Hue, light.Saturation, light.Brightness);

                // Set capabilities
                this._capsByEntity[light.EntityId] = light.Capabilities;

                // Set color temperature data only if supported
                if (light.Capabilities.ColorTemp)
                {
                    this._tempMiredByEntity[light.EntityId] = (light.MinMired, light.MaxMired, light.ColorTempMired);
                }

                PluginLog.Verbose($"[LightStateManager] Initialized {light.EntityId}: isOn={light.IsOn}, HSB=({light.Hue:F1},{light.Saturation:F1},{light.Brightness}), temp={light.ColorTempMired}");
            }

            PluginLog.Info($"[LightStateManager] Light state initialization completed");
        }

        /// <summary>
        /// Sets cached color temperature for a light entity
        /// </summary>
        /// <param name="entityId">Light entity ID</param>
        /// <param name="minM">Optional minimum mireds (preserves existing if null)</param>
        /// <param name="maxM">Optional maximum mireds (preserves existing if null)</param>
        /// <param name="curMired">Current mired value</param>
        public void SetCachedTempMired(String entityId, Int32? minM, Int32? maxM, Int32 curMired)
        {
            var existing = this._tempMiredByEntity.TryGetValue(entityId, out var temp)
                ? temp
                : (Min: DefaultMinMireds, Max: DefaultMaxMireds, Cur: DefaultWarmMired);

            var min = minM ?? existing.Min;
            var max = maxM ?? existing.Max;
            var cur = HSBHelper.Clamp(curMired, min, max);

            this._tempMiredByEntity[entityId] = (min, max, cur);
            PluginLog.Verbose($"[LightStateManager] SetCachedTempMired: {entityId} range=[{min},{max}] cur={cur}");
        }

        /// <summary>
        /// Removes an entity from all internal caches
        /// </summary>
        /// <param name="entityId">Entity ID to remove</param>
        public void RemoveEntity(String entityId)
        {
            this._hsbByEntity.Remove(entityId);
            this._isOnByEntity.Remove(entityId);
            this._capsByEntity.Remove(entityId);
            this._tempMiredByEntity.Remove(entityId);
            PluginLog.Verbose($"[LightStateManager] Removed entity {entityId} from all caches");
        }

        /// <summary>
        /// Gets all currently tracked entity IDs
        /// </summary>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<String> GetTrackedEntityIds() => this._hsbByEntity.Keys.ToList();
    }
}
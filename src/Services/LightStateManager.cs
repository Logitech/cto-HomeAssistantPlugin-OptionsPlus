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
            PluginLog.Verbose(() => $"[LightStateManager] UpdateLightState: {entityId} isOn={isOn} brightness={brightness}");

            this._isOnByEntity[entityId] = isOn;

            if (brightness.HasValue)
            {
                this.SetCachedBrightness(entityId, brightness.Value);
            }
        }

        public void UpdateHsColor(String entityId, Double? hue, Double? saturation)
        {
            PluginLog.Verbose(() => $"[LightStateManager] UpdateHsColor: {entityId} hue={hue} saturation={saturation}");

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
            PluginLog.Verbose(() => $"[LightStateManager] UpdateColorTemp: {entityId} mired={mired} kelvin={kelvin} minM={minM} maxM={maxM}");

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
            PluginLog.Verbose(() => $"[LightStateManager] Set capabilities for {entityId}: onoff={caps.OnOff} bri={caps.Brightness} ctemp={caps.ColorTemp} color={caps.ColorHs}");
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
            PluginLog.Verbose(() => $"[LightStateManager] SetCachedBrightness: {entityId} brightness={brightness}");

            var clampedBrightness = HSBHelper.Clamp(brightness, BrightnessOff, MaxBrightness);

            this._hsbByEntity[entityId] = this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? (hsb.H, hsb.S, clampedBrightness)
                : (DefaultHue, MinSaturation, clampedBrightness);
        }

        public void InitializeLightStates(IEnumerable<LightData> lights)
        {
            var existingCount = this._hsbByEntity.Count;
            var preservedCount = 0;
            var updatedCount = 0;
            
            PluginLog.Info(() => $"[LightStateManager] Initializing light states for {lights.Count()} lights with {existingCount} existing cached states");

            // Backup existing user-adjusted values before updating base state
            var preservedHsb = new Dictionary<String, (Double H, Double S, Int32 B)>(this._hsbByEntity, StringComparer.OrdinalIgnoreCase);
            var preservedTemp = new Dictionary<String, (Int32 Min, Int32 Max, Int32 Cur)>(this._tempMiredByEntity, StringComparer.OrdinalIgnoreCase);
            
            // Only clear capabilities - we'll preserve user state and update base state selectively
            this._capsByEntity.Clear();

            foreach (var light in lights)
            {
                // Always update on/off state from Home Assistant (this is current truth)
                this._isOnByEntity[light.EntityId] = light.IsOn;

                // Always update capabilities from Home Assistant
                this._capsByEntity[light.EntityId] = light.Capabilities;

                // For HSB: preserve existing cached values if they exist (user adjustments), otherwise use HA values
                if (preservedHsb.TryGetValue(light.EntityId, out var existingHsb))
                {
                    // Keep existing cached HSB values (user's last adjustments)
                    this._hsbByEntity[light.EntityId] = existingHsb;
                    preservedCount++;
                    PluginLog.Verbose(() => $"[LightStateManager] PRESERVED cached values for {light.EntityId}: HSB=({existingHsb.H:F1},{existingHsb.S:F1},{existingHsb.B})");
                }
                else
                {
                    // New light or no cached values, use HA state
                    this._hsbByEntity[light.EntityId] = (light.Hue, light.Saturation, light.Brightness);
                    updatedCount++;
                    PluginLog.Verbose(() => $"[LightStateManager] NEW light {light.EntityId}: HSB=({light.Hue:F1},{light.Saturation:F1},{light.Brightness})");
                }

                // For color temperature: preserve cached values if they exist, otherwise use HA values
                if (light.Capabilities.ColorTemp)
                {
                    if (preservedTemp.TryGetValue(light.EntityId, out var existingTemp))
                    {
                        // Keep existing cached temp values, but update min/max from HA if needed
                        this._tempMiredByEntity[light.EntityId] = (light.MinMired, light.MaxMired, existingTemp.Cur);
                        PluginLog.Verbose(() => $"[LightStateManager] PRESERVED cached temp for {light.EntityId}: {existingTemp.Cur} mired");
                    }
                    else
                    {
                        // New temp support or no cached values
                        this._tempMiredByEntity[light.EntityId] = (light.MinMired, light.MaxMired, light.ColorTempMired);
                    }
                }
            }

            PluginLog.Info(() => $"[LightStateManager] State initialization completed: {preservedCount} preserved, {updatedCount} new/updated, {lights.Count()} total");
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
            PluginLog.Verbose(() => $"[LightStateManager] SetCachedTempMired: {entityId} range=[{min},{max}] cur={cur}");
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
            PluginLog.Verbose(() => $"[LightStateManager] Removed entity {entityId} from all caches");
        }

        /// <summary>
        /// Gets all currently tracked entity IDs
        /// </summary>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<String> GetTrackedEntityIds() => this._hsbByEntity.Keys.ToList();
    }
}
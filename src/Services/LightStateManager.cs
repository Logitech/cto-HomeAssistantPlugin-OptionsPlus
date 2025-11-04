namespace Loupedeck.HomeAssistantPlugin.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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

        // Full LightData storage for enhanced functionality
        private readonly Dictionary<String, LightData> _lightData =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Updates the on/off state and optionally brightness for a light.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="isOn">Whether the light is on.</param>
        /// <param name="brightness">Optional brightness value (0-255).</param>
        public void UpdateLightState(String entityId, Boolean isOn, Int32? brightness = null)
        {
            PluginLog.Verbose(() => $"[LightStateManager] UpdateLightState: {entityId} isOn={isOn} brightness={brightness}");

            this._isOnByEntity[entityId] = isOn;

            if (brightness.HasValue)
            {
                this.SetCachedBrightness(entityId, brightness.Value);
            }
        }

        /// <summary>
        /// Updates the hue and saturation values for a light.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="hue">Hue value in degrees (0-360).</param>
        /// <param name="saturation">Saturation value as percentage (0-100).</param>
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

        /// <summary>
        /// Updates the color temperature for a light.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="mired">Color temperature in mireds.</param>
        /// <param name="kelvin">Color temperature in Kelvin.</param>
        /// <param name="minM">Minimum mireds supported.</param>
        /// <param name="maxM">Maximum mireds supported.</param>
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

        /// <summary>
        /// Gets the current HSB values for a light.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <returns>Tuple of hue, saturation, and brightness.</returns>
        public (Double H, Double S, Int32 B) GetHsbValues(String entityId)
        {
            return this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? hsb
                : (DefaultHue, MinSaturation, BrightnessOff);
        }

        /// <summary>
        /// Gets the effective brightness for display (0 if off, cached brightness if on).
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <returns>Brightness value (0-255).</returns>
        public Int32 GetEffectiveBrightness(String entityId)
        {
            // If we know it's OFF, show 0; otherwise show cached B
            return this._isOnByEntity.TryGetValue(entityId, out var on) && !on
                ? BrightnessOff
                : this._hsbByEntity.TryGetValue(entityId, out var hsb) ? hsb.B : BrightnessOff;
        }

        /// <summary>
        /// Checks if a light is currently on.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <returns>True if the light is on.</returns>
        public Boolean IsLightOn(String entityId) => this._isOnByEntity.TryGetValue(entityId, out var isOn) && isOn;

        /// <summary>
        /// Sets the capabilities for a light entity.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="caps">Light capabilities.</param>
        public void SetCapabilities(String entityId, LightCaps caps)
        {
            this._capsByEntity[entityId] = caps;
            PluginLog.Verbose(() => $"[LightStateManager] Set capabilities for {entityId}: onoff={caps.OnOff} bri={caps.Brightness} ctemp={caps.ColorTemp} color={caps.ColorHs}");
        }

        /// <summary>
        /// Gets the capabilities for a light entity.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <returns>Light capabilities.</returns>
        public LightCaps GetCapabilities(String entityId)
        {
            var result = this._capsByEntity.TryGetValue(entityId, out var caps)
                ? caps
                : new LightCaps(true, false, false, false); // Safe default: on/off only
            
            return result;
        }

        /// <summary>
        /// Gets the color temperature range and current value for a light.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <returns>Tuple of min, max, and current mireds, or null if not supported.</returns>
        public (Int32 Min, Int32 Max, Int32 Cur)? GetColorTempMired(String entityId) => this._tempMiredByEntity.TryGetValue(entityId, out var temp) ? temp : null;

        /// <summary>
        /// Sets cached brightness for a light entity.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="brightness">Brightness value (0-255).</param>
        public void SetCachedBrightness(String entityId, Int32 brightness)
        {
            PluginLog.Verbose(() => $"[LightStateManager] SetCachedBrightness: {entityId} brightness={brightness}");

            var clampedBrightness = HSBHelper.Clamp(brightness, BrightnessOff, MaxBrightness);

            this._hsbByEntity[entityId] = this._hsbByEntity.TryGetValue(entityId, out var hsb)
                ? (hsb.H, hsb.S, clampedBrightness)
                : (DefaultHue, MinSaturation, clampedBrightness);
        }

        /// <summary>
        /// Initializes light state from parsed light data.
        /// </summary>
        /// <param name="lights">Collection of light data.</param>
        public void InitializeLightStates(IEnumerable<LightData> lights)
        {
            var existingCount = this._hsbByEntity.Count;
            var preservedCount = 0;
            var updatedCount = 0;

            PluginLog.Info(() => $"[LightStateManager] Initializing light states for {lights.Count()} lights with {existingCount} existing cached states");

            // Backup existing user-adjusted values before updating base state
            var preservedHsb = new Dictionary<String, (Double H, Double S, Int32 B)>(this._hsbByEntity, StringComparer.OrdinalIgnoreCase);
            var preservedTemp = new Dictionary<String, (Int32 Min, Int32 Max, Int32 Cur)>(this._tempMiredByEntity, StringComparer.OrdinalIgnoreCase);

            // Only clear capabilities and light data - we'll preserve user state and update base state selectively
            this._capsByEntity.Clear();
            this._lightData.Clear();

            foreach (var light in lights)
            {
                // Store full LightData object (NEW ENHANCEMENT)
                this._lightData[light.EntityId] = light;

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
            this._lightData.Remove(entityId);
            PluginLog.Verbose(() => $"[LightStateManager] Removed entity {entityId} from all caches including light data");
        }

        /// <summary>
        /// Gets all currently tracked entity IDs
        /// </summary>
        /// <returns>Collection of entity IDs</returns>
        public IEnumerable<String> GetTrackedEntityIds() => this._hsbByEntity.Keys.ToList();

        /// <summary>
        /// Gets all stored light data objects
        /// </summary>
        /// <returns>Collection of all light data</returns>
        public IEnumerable<LightData> GetAllLights()
        {
            return this._lightData.Values.ToList();
        }

        /// <summary>
        /// Gets lights in a specific area
        /// </summary>
        /// <param name="areaId">Area ID to filter by</param>
        /// <returns>Collection of lights in the specified area</returns>
        public IEnumerable<LightData> GetLightsByArea(String areaId)
        {
            if (String.IsNullOrWhiteSpace(areaId))
            {
                return Enumerable.Empty<LightData>();
            }

            return this._lightData.Values
                .Where(light => String.Equals(light.AreaId, areaId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Gets all unique area IDs from stored lights
        /// </summary>
        /// <returns>Collection of distinct area IDs</returns>
        public IEnumerable<String> GetUniqueAreaIds()
        {
            return this._lightData.Values
                .Where(light => !String.IsNullOrWhiteSpace(light.AreaId))
                .Select(light => light.AreaId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Gets specific light's full data
        /// </summary>
        /// <param name="entityId">Entity ID of the light</param>
        /// <returns>Light data if found, null otherwise</returns>
        public LightData? GetLightData(String entityId)
        {
            if (String.IsNullOrWhiteSpace(entityId))
            {
                return null;
            }

            return this._lightData.TryGetValue(entityId, out var lightData) ? lightData : null;
        }

        /// <summary>
        /// Gets area ID to friendly name mapping from stored lights
        /// </summary>
        /// <returns>Dictionary mapping area IDs to friendly names</returns>
        public Dictionary<String, String> GetAreaIdToNameMapping()
        {
            var areaMapping = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            // Extract unique areas and their names from stored light data
            var areaGroups = this._lightData.Values
                .Where(light => !String.IsNullOrWhiteSpace(light.AreaId))
                .GroupBy(light => light.AreaId, StringComparer.OrdinalIgnoreCase);

            foreach (var group in areaGroups)
            {
                var areaId = group.Key;
                // Use the area name from any light in the area (they should all have the same area name)
                var firstLight = group.FirstOrDefault();
                if (firstLight != null)
                {
                    // Extract area name from device name or use area ID as fallback
                    var areaName = firstLight.AreaId; // This could be enhanced to get actual friendly names from registry
                    areaMapping[areaId] = areaName;
                }
            }

            return areaMapping;
        }

        /// <summary>
        /// Initializes or updates light states by fetching data from Home Assistant
        /// This method handles all the data fetching and parsing internally
        /// </summary>
        /// <param name="dataService">Service for fetching data from Home Assistant</param>
        /// <param name="dataParser">Service for parsing Home Assistant data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task with success status and optional error message</returns>
        public async Task<(Boolean Success, String? ErrorMessage)> InitOrUpdateAsync(
            IHomeAssistantDataService dataService,
            IHomeAssistantDataParser dataParser,
            CancellationToken cancellationToken = default)
        {
            try
            {
                PluginLog.Info("[LightStateManager] InitOrUpdateAsync: Starting self-contained initialization");

                

                // Fetch all required data from Home Assistant APIs
                var (okStates, statesJson, errStates) = await dataService.FetchStatesAsync(cancellationToken).ConfigureAwait(false);
                if (!okStates)
                {
                    var errorMsg = $"Failed to fetch states: {errStates}";
                    PluginLog.Error($"[LightStateManager] InitOrUpdateAsync: {errorMsg}");
                    return (false, errorMsg);
                }

                // Validate statesJson is not null even if fetch succeeded
                if (String.IsNullOrEmpty(statesJson))
                {
                    const String errorMsg = "FetchStatesAsync succeeded but returned null or empty JSON data";
                    PluginLog.Error($"[LightStateManager] InitOrUpdateAsync: {errorMsg}");
                    return (false, errorMsg);
                }

                // Fetch registry data (optional - don't fail if these aren't available)
                var (okEnt, entJson, errEnt) = await dataService.FetchEntityRegistryAsync(cancellationToken).ConfigureAwait(false);
                var (okDev, devJson, errDev) = await dataService.FetchDeviceRegistryAsync(cancellationToken).ConfigureAwait(false);
                var (okArea, areaJson, errArea) = await dataService.FetchAreaRegistryAsync(cancellationToken).ConfigureAwait(false);

                if (!okEnt || !okDev || !okArea)
                {
                    PluginLog.Warning($"[LightStateManager] InitOrUpdateAsync: Some registry data unavailable (ent:{okEnt}, dev:{okDev}, area:{okArea}) - continuing with basic initialization");
                }

                // Parse registry data
                var registryData = dataParser.ParseRegistries(devJson, entJson, areaJson);

                // Parse light states - this gives us complete LightData objects
                var lights = dataParser.ParseLightStates(statesJson, registryData);

                // Initialize light state manager with parsed lights
                this.InitializeLightStates(lights);

                PluginLog.Info($"[LightStateManager] InitOrUpdateAsync: Successfully initialized with {lights.Count} lights");
                return (true, null);
            }
            catch (Exception ex)
            {
                var errorMsg = $"InitOrUpdateAsync failed: {ex.Message}";
                PluginLog.Error(ex, $"[LightStateManager] {errorMsg}");
                return (false, errorMsg);
            }
        }
    }
}
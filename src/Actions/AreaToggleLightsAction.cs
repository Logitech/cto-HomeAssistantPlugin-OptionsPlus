namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;
    using Loupedeck.HomeAssistantPlugin.Models;
    using Loupedeck.HomeAssistantPlugin.Services;

    /// <summary>
    /// Area-based action for toggling all Home Assistant lights in a selected area.
    /// Supports brightness, color temperature, hue/saturation, and white level adjustments.
    /// Uses individual light capability filtering to send maximum possible settings to each light.
    /// </summary>
    public sealed class AreaToggleLightsAction : ActionEditorCommand, IDisposable
    {
        /// <summary>
        /// Logging prefix for this action's log messages.
        /// </summary>
        private const String LogPrefix = "[AreaToggleLights]";

        /// <summary>
        /// Home Assistant client interface for WebSocket communication.
        /// </summary>
        private IHaClient? _ha;
        
        /// <summary>
        /// Light control service with debounced adjustments.
        /// </summary>
        private ILightControlService? _lightSvc;
        
        /// <summary>
        /// Light state manager for tracking light properties and capabilities.
        /// </summary>
        private ILightStateManager? _lightStateManager;
        
        /// <summary>
        /// Data service for fetching Home Assistant entity states.
        /// </summary>
        private IHomeAssistantDataService? _dataService;
        
        /// <summary>
        /// Data parser for processing Home Assistant JSON responses.
        /// </summary>
        private IHomeAssistantDataParser? _dataParser;
        

        /// <summary>
        /// Capability service for analyzing light feature support.
        /// </summary>
        private readonly CapabilityService _capSvc = new();
        
        /// <summary>
        /// Indicates whether this instance has been disposed.
        /// </summary>
        private Boolean _disposed = false;

        /// <summary>
        /// Control name for area selection dropdown.
        /// </summary>
        private const String ControlArea = "ha_area";
        
        /// <summary>
        /// Control name for brightness adjustment (0-255).
        /// </summary>
        private const String ControlBrightness = "ha_brightness";
        
        /// <summary>
        /// Control name for color temperature adjustment (2000K-6500K).
        /// </summary>
        private const String ControlTemperature = "ha_temperature";
        
        /// <summary>
        /// Control name for hue adjustment (0-360 degrees).
        /// </summary>
        private const String ControlHue = "ha_hue";
        
        /// <summary>
        /// Control name for saturation adjustment (0-100%).
        /// </summary>
        private const String ControlSaturation = "ha_saturation";
        
        /// <summary>
        /// Control name for white level adjustment (0-255) for RGBW lights or warm white in RGBWW.
        /// </summary>
        private const String ControlWhiteLevel = "ha_white_level";
        
        /// <summary>
        /// Control name for cold white level adjustment (0-255) for RGBWW lights.
        /// </summary>
        private const String ControlColdWhiteLevel = "ha_cold_white_level";

        /// <summary>
        /// Minimum brightness value supported by Home Assistant (1-255 range).
        /// </summary>
        private const Int32 MinBrightness = 1;
        
        /// <summary>
        /// Maximum brightness value supported by Home Assistant (1-255 range).
        /// </summary>
        private const Int32 MaxBrightness = 255;
        
        /// <summary>
        /// Minimum color temperature in Kelvin (warm white).
        /// </summary>
        private const Int32 MinTemperature = 2000;
        
        /// <summary>
        /// Maximum color temperature in Kelvin (cool white).
        /// </summary>
        private const Int32 MaxTemperature = 6500;
        
        /// <summary>
        /// Minimum hue value in degrees (0-360 range).
        /// </summary>
        private const Double MinHue = 0.0;
        
        /// <summary>
        /// Maximum hue value in degrees (0-360 range).
        /// </summary>
        private const Double MaxHue = 360.0;
        
        /// <summary>
        /// Minimum saturation value as percentage (0-100 range).
        /// </summary>
        private const Double MinSaturation = 0.0;
        
        /// <summary>
        /// Maximum saturation value as percentage (0-100 range).
        /// </summary>
        private const Double MaxSaturation = 100.0;
        
        /// <summary>
        /// Full color value used for HSB to RGB conversion (100%).
        /// </summary>
        private const Double FullColorValue = 100.0;
        
        /// <summary>
        /// Authentication timeout in seconds for Home Assistant connections.
        /// </summary>
        private const Int32 AuthTimeoutSeconds = 8;
        
        /// <summary>
        /// Debounce interval in milliseconds for light control adjustments.
        /// </summary>
        private const Int32 DebounceMs = 100;

        /// <summary>
        /// Icon service for rendering action button graphics.
        /// </summary>
        private readonly IconService _icons;

        /// <summary>
        /// Area mapping: Area ID to friendly name from registry data.
        /// </summary>
        private readonly Dictionary<String, String> _areaIdToName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Entity mapping: Entity ID to Area ID from lights data.
        /// </summary>
        private readonly Dictionary<String, String> _entityToAreaId = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="AreaToggleLightsAction"/> class.
        /// Sets up action editor controls for area selection and parameter configuration.
        /// </summary>
        public AreaToggleLightsAction()
        {
            this.Name = "HomeAssistant.AreaToggleLights";
            this.DisplayName = "Advanced Toggle Area Lights";
            this.GroupName = "Lights";
            this.Description = "Toggle all lights in a Home Assistant area with advanced controls for brightness, color, and temperature.";

            // Area selection dropdown (replaces individual light selection)
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlArea, "Area (retry if empty)"));

            // Parameter controls (identical to AdvancedToggleLights)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlBrightness, "Brightness (0-255)")
                    .SetPlaceholder("128")
            );

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlTemperature, "Temperature (2000K-6500K)")
                    .SetPlaceholder("3000")
            );

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlHue, "Hue (0-360°)")
                    .SetPlaceholder("0")
            );

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlSaturation, "Saturation (0-100%)")
                    .SetPlaceholder("100")
            );

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlWhiteLevel, "White Level / Warm White (0-255)")
                    .SetPlaceholder("255")
            );

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlColdWhiteLevel, "Cold White Level (0-255)")
                    .SetPlaceholder("255")
            );

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Area, "area_icon.svg" }
            });

            PluginLog.Info($"{LogPrefix} Constructor completed - dependency initialization deferred to OnLoad()");
        }

        /// <summary>
        /// Gets the command image for the action button.
        /// </summary>
        /// <param name="parameters">Action editor parameters.</param>
        /// <param name="width">Requested image width.</param>
        /// <param name="height">Requested image height.</param>
        /// <returns>Bitmap image showing an area icon.</returns>
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height) =>
            // Show area icon for area-based control
            this._icons.Get(IconId.Area);

        /// <summary>
        /// Loads the action and initializes service dependencies using modern dependency injection pattern.
        /// Creates adapters for Home Assistant client, data services, and light control.
        /// </summary>
        /// <returns><c>true</c> if initialization succeeded; otherwise, <c>false</c>.</returns>
        protected override Boolean OnLoad()
        {
            PluginLog.Info($"{LogPrefix} OnLoad() START");

            try
            {
                if (this.Plugin is HomeAssistantPlugin haPlugin)
                {
                    PluginLog.Info($"{LogPrefix} Initializing dependencies using modern service architecture");

                    // Initialize dependency injection - use the shared HaClient from Plugin
                    this._ha = new HaClientAdapter(haPlugin.HaClient);
                    this._dataService = new HomeAssistantDataService(this._ha);
                    this._dataParser = new HomeAssistantDataParser(this._capSvc);

                    // Use the singleton LightStateManager from the main plugin
                    this._lightStateManager = haPlugin.LightStateManager;
                    var existingCount = this._lightStateManager.GetTrackedEntityIds().Count();
                    PluginLog.Info($"{LogPrefix} Using singleton LightStateManager with {existingCount} existing tracked entities");


                    // Initialize light control service with debounce settings
                    this._lightSvc = new LightControlService(
                        this._ha,
                        DebounceMs,
                        DebounceMs,
                        DebounceMs
                    );

                    PluginLog.Info($"{LogPrefix} All dependencies initialized successfully");
                    return true;
                }
                else
                {
                    PluginLog.Error($"{LogPrefix} Plugin is not HomeAssistantPlugin, actual type: {this.Plugin?.GetType()?.Name ?? "null"}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} OnLoad() failed with exception");
                return false;
            }
        }

        /// <summary>
        /// Unloads the action and disposes of resources.
        /// </summary>
        /// <returns>Always <c>true</c> indicating successful unload.</returns>
        protected override Boolean OnUnload()
        {
            PluginLog.Info($"{LogPrefix} OnUnload()");
            this.Dispose();
            return true;
        }

        /// <summary>
        /// Disposes of managed resources, particularly the light control service.
        /// Shared services are managed by the main plugin and are not disposed here.
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} Disposing resources");

            try
            {
                this._lightSvc?.Dispose();
                this._lightSvc = null;

                // Don't dispose shared services - they're managed by the main plugin
                this._ha = null;
                this._dataService = null;
                this._dataParser = null;
                this._lightStateManager = null;

                this._disposed = true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} Error during disposal");
            }
        }

        /// <summary>
        /// Ensures Home Assistant connection is established and authenticated.
        /// Validates configuration settings and attempts connection if not already authenticated.
        /// </summary>
        /// <returns><c>true</c> if connection is ready; otherwise, <c>false</c>.</returns>
        private async Task<Boolean> EnsureHaReadyAsync()
        {
            if (this._ha == null)
            {
                PluginLog.Error($"{LogPrefix} EnsureHaReady: HaClient not initialized");
                return false;
            }

            if (this._ha.IsAuthenticated)
            {
                PluginLog.Info($"{LogPrefix} EnsureHaReady: already authenticated");
                return true;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) ||
                String.IsNullOrWhiteSpace(baseUrl))
            {
                PluginLog.Warning($"{LogPrefix} EnsureHaReady: Missing ha.baseUrl setting");
                HealthBus.Error("Missing Base URL");
                return false;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var token) ||
                String.IsNullOrWhiteSpace(token))
            {
                PluginLog.Warning($"{LogPrefix} EnsureHaReady: Missing ha.token setting");
                HealthBus.Error("Missing Token");
                return false;
            }

            try
            {
                PluginLog.Info($"{LogPrefix} Connecting to HA using modern service architecture… url='{baseUrl}'");
                var (ok, msg) = await this._ha.ConnectAndAuthenticateAsync(
                    baseUrl, token, TimeSpan.FromSeconds(AuthTimeoutSeconds), CancellationToken.None
                ).ConfigureAwait(false);

                if (ok)
                {
                    HealthBus.Ok("Auth OK");
                    PluginLog.Info($"{LogPrefix} Auth result ok={ok} msg='{msg}'");
                }
                else
                {
                    HealthBus.Error(msg ?? "Auth failed");
                    PluginLog.Warning($"{LogPrefix} Auth failed: {msg}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} EnsureHaReady exception");
                HealthBus.Error("Auth error");
                return false;
            }
        }

        /// <summary>
        /// Executes the area toggle lights command with comprehensive light control.
        /// Processes all lights in the selected area with brightness, color temperature, hue/saturation, and white level parameters.
        /// Uses individual light capability filtering to send maximum possible settings to each light.
        /// </summary>
        /// <param name="ps">Action editor parameters containing user-configured values.</param>
        /// <returns><c>true</c> if all light operations succeeded; otherwise, <c>false</c>.</returns>
        protected override Boolean RunCommand(ActionEditorActionParameters ps)
        {
            try
            {
                PluginLog.Info($"{LogPrefix} RunCommand START");

                // Make sure we're online before doing anything
                if (!this.EnsureHaReadyAsync().GetAwaiter().GetResult())
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: EnsureHaReady failed");
                    return false;
                }

                // Get selected area
                if (!ps.TryGetString(ControlArea, out var selectedArea) || String.IsNullOrWhiteSpace(selectedArea))
                {
                    PluginLog.Warning($"{LogPrefix} No area selected");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "No area selected");
                    return false;
                }

                // Validate area exists using internal cache
                if (!this._areaIdToName.ContainsKey(selectedArea))
                {
                    PluginLog.Warning($"{LogPrefix} Selected area '{selectedArea}' does not exist in cache");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"Area '{selectedArea}' not found");
                    return false;
                }

                // Get all available lights (from LightStateManager)
                var allLights = this._lightStateManager?.GetTrackedEntityIds() ?? Enumerable.Empty<String>();
                
                // Get lights in selected area using internal cache
                var areaLights = allLights.Where(entityId =>
                    this._entityToAreaId.TryGetValue(entityId, out var lightAreaId) &&
                    String.Equals(lightAreaId, selectedArea, StringComparison.OrdinalIgnoreCase)
                ).ToList();
                
                if (!areaLights.Any())
                {
                    var areaName = this._areaIdToName.TryGetValue(selectedArea, out var name) ? name : selectedArea;
                    PluginLog.Warning($"{LogPrefix} No lights found in area '{areaName}'");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"No lights found in area '{areaName}'");
                    return false;
                }

                PluginLog.Info($"{LogPrefix} Processing {areaLights.Count} lights in area '{selectedArea}'");

                // Parse control values using defined constants
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, MaxBrightness);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, MinTemperature, MaxTemperature);
                var hue = this.ParseDoubleParameter(ps, ControlHue, MinHue, MaxHue);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, MinSaturation, MaxSaturation);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, MaxBrightness);
                var coldWhiteLevel = this.ParseIntParameter(ps, ControlColdWhiteLevel, 0, MaxBrightness);

                // Process lights with individual capability filtering
                var success = this.ProcessAreaLights(areaLights, brightness, temperature, hue, saturation, whiteLevel, coldWhiteLevel);

                PluginLog.Info($"{LogPrefix} RunCommand completed with success={success}");
                return success;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} RunCommand exception");
                return false;
            }
            finally
            {
                PluginLog.Info($"{LogPrefix} RunCommand END");
            }
        }

        /// <summary>
        /// Processes all lights in the area with individual capability filtering.
        /// Key difference from AdvancedToggleLights: processes each light with its own capabilities instead of intersection.
        /// </summary>
        /// <param name="areaLights">Collection of light entity IDs in the area.</param>
        /// <param name="brightness">Brightness value (0-255) or null.</param>
        /// <param name="temperature">Color temperature in Kelvin or null.</param>
        /// <param name="hue">Hue value (0-360) or null.</param>
        /// <param name="saturation">Saturation value (0-100) or null.</param>
        /// <param name="whiteLevel">White level (0-255) or null.</param>
        /// <param name="coldWhiteLevel">Cold white level (0-255) or null.</param>
        /// <returns><c>true</c> if all lights processed successfully; otherwise, <c>false</c>.</returns>
        private Boolean ProcessAreaLights(IEnumerable<String> areaLights, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel, Int32? coldWhiteLevel)
        {
            var success = true;
            
            foreach (var entityId in areaLights)
            {
                try
                {
                    // Get INDIVIDUAL capabilities for this specific light
                    var individualCaps = this._lightStateManager?.GetCapabilities(entityId) 
                        ?? new LightCaps(true, false, false, false, null);
                        
                    // Process this light with ITS OWN capabilities
                    success &= this.ProcessSingleLight(entityId, individualCaps, brightness, temperature, hue, saturation, whiteLevel, coldWhiteLevel);
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, $"{LogPrefix} Failed to process light {entityId}");
                    success = false;
                }
            }
            
            return success;
        }

        /// <summary>
        /// Parses and validates an integer parameter from action editor parameters.
        /// Clamps the value to the specified range and logs warnings for invalid inputs.
        /// </summary>
        /// <param name="ps">Action editor parameters.</param>
        /// <param name="controlName">Name of the control to parse.</param>
        /// <param name="min">Minimum allowed value (inclusive).</param>
        /// <param name="max">Maximum allowed value (inclusive).</param>
        /// <returns>Parsed and clamped integer value, or <c>null</c> if parsing failed or value was empty.</returns>
        private Int32? ParseIntParameter(ActionEditorActionParameters ps, String controlName, Int32 min, Int32 max)
        {
            if (!ps.TryGetString(controlName, out var valueStr) || String.IsNullOrWhiteSpace(valueStr))
            {
                return null;
            }

            if (Int32.TryParse(valueStr, out var value))
            {
                var clamped = HSBHelper.Clamp(value, min, max);
                if (clamped != value)
                {
                    PluginLog.Debug($"{LogPrefix} Parameter {controlName}: {value} clamped to {clamped} (range {min}-{max})");
                }
                return clamped;
            }

            PluginLog.Warning($"{LogPrefix} Parameter {controlName}: failed to parse '{valueStr}' as integer");
            return null;
        }

        /// <summary>
        /// Parses and validates a double parameter from action editor parameters.
        /// Clamps the value to the specified range and logs warnings for invalid inputs.
        /// </summary>
        /// <param name="ps">Action editor parameters.</param>
        /// <param name="controlName">Name of the control to parse.</param>
        /// <param name="min">Minimum allowed value (inclusive).</param>
        /// <param name="max">Maximum allowed value (inclusive).</param>
        /// <returns>Parsed and clamped double value, or <c>null</c> if parsing failed or value was empty.</returns>
        private Double? ParseDoubleParameter(ActionEditorActionParameters ps, String controlName, Double min, Double max)
        {
            if (!ps.TryGetString(controlName, out var valueStr) || String.IsNullOrWhiteSpace(valueStr))
            {
                return null;
            }

            if (Double.TryParse(valueStr, out var value))
            {
                var clamped = HSBHelper.Clamp(value, min, max);
                if (Math.Abs(clamped - value) > 1e-6)
                {
                    PluginLog.Debug($"{LogPrefix} Parameter {controlName}: {value} clamped to {clamped} (range {min}-{max})");
                }
                return clamped;
            }

            PluginLog.Warning($"{LogPrefix} Parameter {controlName}: failed to parse '{valueStr}' as double");
            return null;
        }

        /// <summary>
        /// Processes a single light with its individual capabilities.
        /// This is the core difference from AdvancedToggleLights - uses individual capabilities instead of intersection.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="individualCaps">Individual capabilities of this specific light.</param>
        /// <param name="brightness">Brightness value or null.</param>
        /// <param name="temperature">Color temperature or null.</param>
        /// <param name="hue">Hue value or null.</param>
        /// <param name="saturation">Saturation value or null.</param>
        /// <param name="whiteLevel">White level or null.</param>
        /// <param name="coldWhiteLevel">Cold white level or null.</param>
        /// <returns><c>true</c> if light processed successfully; otherwise, <c>false</c>.</returns>
        private Boolean ProcessSingleLight(String entityId, LightCaps individualCaps, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel, Int32? coldWhiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");
            PluginLog.Info($"{LogPrefix} Individual capabilities: onoff={individualCaps.OnOff} brightness={individualCaps.Brightness} colorTemp={individualCaps.ColorTemp} colorHs={individualCaps.ColorHs}");

            var preferredColorMode = individualCaps.PreferredColorMode ?? "hs";
            PluginLog.Info($"{LogPrefix} Input parameters: brightness={brightness} temperature={temperature}K hue={hue}° saturation={saturation}% whiteLevel={whiteLevel} coldWhiteLevel={coldWhiteLevel}");

            if (this._lightSvc == null)
            {
                PluginLog.Error($"{LogPrefix} ProcessSingleLight: LightControlService not available");
                return false;
            }

            // If no parameters specified, just toggle
            if (!brightness.HasValue && !temperature.HasValue && !hue.HasValue && !saturation.HasValue && !whiteLevel.HasValue && !coldWhiteLevel.HasValue)
            {
                PluginLog.Info($"{LogPrefix} No parameters provided, using simple toggle for '{entityId}'");
                var success = this._lightSvc.ToggleAsync(entityId).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: toggle entity_id={entityId} -> success={success}");

                if (success && this._lightStateManager != null)
                {
                    // Update local state - toggle the current state
                    var wasOn = this._lightStateManager.IsLightOn(entityId);
                    this._lightStateManager.UpdateLightState(entityId, !wasOn);
                    PluginLog.Info($"{LogPrefix} Updated local state: {entityId} toggled from {wasOn} to {!wasOn}");
                }

                if (!success)
                {
                    var friendlyName = entityId; // Could be enhanced to get friendly name
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        $"Failed to toggle light {friendlyName}");
                }

                return success;
            }

            // Check current light state for proper toggle behavior when parameters are provided
            var isCurrentlyOn = this._lightStateManager?.IsLightOn(entityId) ?? false;
            PluginLog.Info($"{LogPrefix} Current light state for {entityId}: isOn={isCurrentlyOn}");

            // If light is currently ON and we have parameters, turn it OFF (toggle behavior)
            if (isCurrentlyOn)
            {
                PluginLog.Info($"{LogPrefix} Light {entityId} is ON, turning OFF for toggle behavior");
                var success = this._lightSvc.TurnOffAsync(entityId).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: turn_off entity_id={entityId} -> success={success}");

                if (success && this._lightStateManager != null)
                {
                    // Update local state - light is now off
                    this._lightStateManager.UpdateLightState(entityId, false);
                    PluginLog.Info($"{LogPrefix} Updated local state: {entityId} turned OFF");
                }

                if (!success)
                {
                    var friendlyName = entityId; // Could be enhanced to get friendly name
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        $"Failed to turn off light {friendlyName}");
                }

                return success;
            }

            // Light is OFF, turn it ON with the specified parameters
            PluginLog.Info($"{LogPrefix} Light {entityId} is OFF, turning ON with parameters");

            // Build service call data based on INDIVIDUAL capabilities
            var serviceData = new Dictionary<String, Object>();

            // Add brightness if THIS light supports it and parameter is specified
            if (brightness.HasValue && individualCaps.Brightness)
            {
                var bri = HSBHelper.Clamp(brightness.Value, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added brightness for {entityId}: {bri}");
            }
            else if (brightness.HasValue && !individualCaps.Brightness)
            {
                PluginLog.Info($"{LogPrefix} Skipping brightness for {entityId} - not supported");
            }
            else if ((whiteLevel.HasValue || coldWhiteLevel.HasValue) && individualCaps.Brightness &&
                     !preferredColorMode.EqualsNoCase("rgbw") && !preferredColorMode.EqualsNoCase("rgbww"))
            {
                // White level as fallback brightness (only for non-RGBW/RGBWW lights)
                var whiteValue = whiteLevel ?? coldWhiteLevel ?? 0;
                var bri = HSBHelper.Clamp(whiteValue, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added white level as brightness fallback for {entityId}: {whiteValue} -> {bri}");
            }

            // Add color temp if THIS light supports it
            if (temperature.HasValue && individualCaps.ColorTemp)
            {
                var kelvin = HSBHelper.Clamp(temperature.Value, MinTemperature, MaxTemperature);
                var mired = ColorTemp.KelvinToMired(kelvin);
                serviceData["color_temp"] = mired;
                PluginLog.Info($"{LogPrefix} Added color temp for {entityId}: {kelvin}K -> {mired} mireds");
            }
            else if (temperature.HasValue && !individualCaps.ColorTemp)
            {
                PluginLog.Info($"{LogPrefix} Skipping color temp for {entityId} - not supported");
            }

            // Add hue/saturation if THIS light supports it
            if (hue.HasValue && saturation.HasValue && individualCaps.ColorHs)
            {
                var h = HSBHelper.Wrap360(hue.Value);
                var s = HSBHelper.Clamp(saturation.Value, MinSaturation, MaxSaturation);

                // Use the preferred color mode for this light
                PluginLog.Info($"{LogPrefix} Using preferred color mode for {entityId}: {preferredColorMode}");

                switch (preferredColorMode.ToLowerInvariant())
                {
                    case "rgbww":
                        // Convert HS to RGBWW (R,G,B,ColdWhite,WarmWhite)
                        var (r1, g1, b1) = HSBHelper.HsbToRgb(h, s, FullColorValue);
                        
                        var coldWhite = 0;
                        var warmWhite = 0;
                        
                        if (coldWhiteLevel.HasValue || whiteLevel.HasValue)
                        {
                            if (coldWhiteLevel.HasValue && whiteLevel.HasValue)
                            {
                                coldWhite = HSBHelper.Clamp(coldWhiteLevel.Value, 0, MaxBrightness);
                                warmWhite = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                                PluginLog.Info($"{LogPrefix} Using separate white levels for {entityId}: cold={coldWhite}, warm={warmWhite}");
                            }
                            else if (coldWhiteLevel.HasValue)
                            {
                                coldWhite = HSBHelper.Clamp(coldWhiteLevel.Value, 0, MaxBrightness);
                                warmWhite = coldWhite;
                                PluginLog.Info($"{LogPrefix} Only cold white specified for {entityId}: using {coldWhite} for both channels");
                            }
                            else if (whiteLevel.HasValue)
                            {
                                warmWhite = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                                coldWhite = warmWhite;
                                PluginLog.Info($"{LogPrefix} Only warm white specified for {entityId}: using {warmWhite} for both channels");
                            }
                        }
                        
                        serviceData["rgbww_color"] = new Object[] { r1, g1, b1, coldWhite, warmWhite };
                        PluginLog.Info($"{LogPrefix} Added rgbww_color for {entityId}: HS({h:F1}°,{s:F1}%) -> RGBWW({r1},{g1},{b1},{coldWhite},{warmWhite})");
                        break;

                    case "rgbw":
                        // Convert HS to RGBW (R,G,B,White)
                        var (r2, g2, b2) = HSBHelper.HsbToRgb(h, s, FullColorValue);
                        
                        var whiteChannel = 0;
                        if (whiteLevel.HasValue)
                        {
                            whiteChannel = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                        }
                        
                        serviceData["rgbw_color"] = new Object[] { r2, g2, b2, whiteChannel };
                        PluginLog.Info($"{LogPrefix} Added rgbw_color for {entityId}: HS({h:F1}°,{s:F1}%) + white({whiteLevel}) -> RGBW({r2},{g2},{b2},{whiteChannel})");
                        break;

                    case "rgb":
                        // Convert HS to RGB
                        var (r3, g3, b3) = HSBHelper.HsbToRgb(h, s, FullColorValue);
                        serviceData["rgb_color"] = new Object[] { r3, g3, b3 };
                        PluginLog.Info($"{LogPrefix} Added rgb_color for {entityId}: HS({h:F1}°,{s:F1}%) -> RGB({r3},{g3},{b3})");
                        break;

                    case "hs":
                    default:
                        // Use HS color (force decimal serialization while keeping in valid ranges)
                        var hueForJson = Math.Abs(h % 1.0) < 0.001 ?
                            (h >= 359.9 ? h - 0.0001 : h + 0.0001) : h;
                        var satForJson = Math.Abs(s % 1.0) < 0.001 ?
                            (s >= 99.9 ? s - 0.0001 : s + 0.0001) : s;
                        serviceData["hs_color"] = new Object[] { hueForJson, satForJson };
                        PluginLog.Info($"{LogPrefix} Added hs_color for {entityId}: HS({h:F1}°,{s:F1}%) -> HS({hueForJson:F4}°,{satForJson:F4}%)");
                        break;
                }
            }
            else if ((hue.HasValue || saturation.HasValue) && !individualCaps.ColorHs)
            {
                PluginLog.Info($"{LogPrefix} Skipping hue/saturation for {entityId} - not supported");
            }

            // Send service calls using the same separation pattern as AdvancedToggleLights
            return this.SendLightServiceCalls(entityId, serviceData, brightness, temperature, hue, saturation, whiteLevel);
        }

        /// <summary>
        /// Sends separated service calls for better compatibility with various light types.
        /// Uses the same pattern as AdvancedToggleLights with brightness, temperature, and color calls.
        /// </summary>
        /// <param name="entityId">Light entity ID.</param>
        /// <param name="serviceData">Service call data dictionary.</param>
        /// <param name="brightness">Original brightness parameter.</param>
        /// <param name="temperature">Original temperature parameter.</param>
        /// <param name="hue">Original hue parameter.</param>
        /// <param name="saturation">Original saturation parameter.</param>
        /// <param name="whiteLevel">Original white level parameter.</param>
        /// <returns><c>true</c> if all service calls succeeded; otherwise, <c>false</c>.</returns>
        private Boolean SendLightServiceCalls(String entityId, Dictionary<String, Object> serviceData,
            Int32? brightness, Int32? temperature, Double? hue, Double? saturation, Int32? whiteLevel)
        {
            var overallSuccess = true;

            if (serviceData.Any())
            {
                // Separate brightness and color attributes for better compatibility
                var brightnessData = new Dictionary<String, Object>();
                var colorData = new Dictionary<String, Object>();
                var tempData = new Dictionary<String, Object>();

                // Separate the attributes
                foreach (var kvp in serviceData)
                {
                    switch (kvp.Key)
                    {
                        case "brightness":
                            brightnessData[kvp.Key] = kvp.Value;
                            break;
                        case "color_temp":
                            tempData[kvp.Key] = kvp.Value;
                            break;
                        case "hs_color":
                        case "rgb_color":
                        case "rgbw_color":
                        case "rgbww_color":
                        case "xy_color":
                            colorData[kvp.Key] = kvp.Value;
                            break;
                        default:
                            brightnessData[kvp.Key] = kvp.Value; // fallback to brightness call
                            break;
                    }
                }

                PluginLog.Info($"{LogPrefix} Separated into {brightnessData.Count} brightness attrs, {colorData.Count} color attrs, {tempData.Count} temp attrs for {entityId}");

                // 1. First call: Turn on with brightness (most compatible)
                if (brightnessData.Any())
                {
                    var briData = JsonSerializer.SerializeToElement(brightnessData);
                    var briJson = JsonSerializer.Serialize(briData);
                    PluginLog.Info($"{LogPrefix} CALL 1/3: Brightness for {entityId} - {briJson}");

                    var briSuccess = this._lightSvc.TurnOnAsync(entityId, briData).GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} HA SERVICE CALL 1: turn_on entity_id={entityId} data={briJson} -> success={briSuccess}");
                    overallSuccess &= briSuccess;

                    if (briSuccess)
                    {
                        Thread.Sleep(50);
                    }
                }
                else
                {
                    // Turn on without parameters first
                    PluginLog.Info($"{LogPrefix} CALL 1/3: Simple turn_on for {entityId} (no brightness specified)");
                    var onSuccess = this._lightSvc.TurnOnAsync(entityId).GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} HA SERVICE CALL 1: turn_on entity_id={entityId} -> success={onSuccess}");
                    overallSuccess &= onSuccess;

                    if (onSuccess)
                    {
                        Thread.Sleep(50);
                    }
                }

                // 2. Second call: Color temperature (if specified)
                if (tempData.Any())
                {
                    var tempDataElement = JsonSerializer.SerializeToElement(tempData);
                    var tempJson = JsonSerializer.Serialize(tempDataElement);
                    PluginLog.Info($"{LogPrefix} CALL 2/3: Temperature for {entityId} - {tempJson}");

                    var tempSuccess = this._lightSvc.TurnOnAsync(entityId, tempDataElement).GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} HA SERVICE CALL 2: turn_on entity_id={entityId} data={tempJson} -> success={tempSuccess}");
                    overallSuccess &= tempSuccess;

                    if (tempSuccess && colorData.Any())
                    {
                        Thread.Sleep(50);
                    }
                }

                // 3. Third call: Color (if specified and no temp conflict)
                if (colorData.Any())
                {
                    var colorDataElement = JsonSerializer.SerializeToElement(colorData);
                    var colorJson = JsonSerializer.Serialize(colorDataElement);
                    PluginLog.Info($"{LogPrefix} CALL 3/3: Color for {entityId} - {colorJson}");

                    var colorSuccess = this._lightSvc.TurnOnAsync(entityId, colorDataElement).GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} HA SERVICE CALL 3: turn_on entity_id={entityId} data={colorJson} -> success={colorSuccess}");
                    overallSuccess &= colorSuccess;
                }

                // Update local state if any call succeeded
                if (overallSuccess && this._lightStateManager != null)
                {
                    // Update local state - light is now on with the specified brightness
                    var brightnessValue = brightness ?? whiteLevel;
                    this._lightStateManager.UpdateLightState(entityId, true, brightnessValue);

                    // Update HSB values if hue/saturation were specified
                    if (hue.HasValue || saturation.HasValue)
                    {
                        this._lightStateManager.UpdateHsColor(entityId, hue, saturation);
                    }

                    // Update color temperature if specified
                    if (temperature.HasValue)
                    {
                        var kelvin = HSBHelper.Clamp(temperature.Value, MinTemperature, MaxTemperature);
                        this._lightStateManager.UpdateColorTemp(entityId, null, kelvin, null, null);
                    }

                    PluginLog.Info($"{LogPrefix} Updated local state: {entityId} turned ON with parameters");
                }

                if (!overallSuccess)
                {
                    var friendlyName = entityId; // Could be enhanced to get friendly name
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        $"Failed to control light {friendlyName}");
                }

                return overallSuccess;
            }
            else
            {
                // No valid parameters after capability filtering, just turn on without parameters
                PluginLog.Info($"{LogPrefix} No valid parameters after capability check, turning ON without specific parameters for '{entityId}'");
                var success = this._lightSvc.TurnOnAsync(entityId).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: turn_on entity_id={entityId} data=null -> success={success}");

                if (success && this._lightStateManager != null)
                {
                    // Update local state - light is now on (no specific brightness)
                    this._lightStateManager.UpdateLightState(entityId, true);
                    PluginLog.Info($"{LogPrefix} Updated local state: {entityId} turned ON without parameters");
                }

                if (!success)
                {
                    var friendlyName = entityId; // Could be enhanced to get friendly name
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        $"Failed to turn on light {friendlyName}");
                }

                return success;
            }
        }

        /// <summary>
        /// Populates the area dropdown with areas that contain lights.
        /// PERFORMANCE FIX: Cache-first approach - populate immediately from cache if available, then refresh in background.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event arguments containing dropdown information.</param>
        private void OnListboxItemsRequested(Object? sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlArea))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested for areas - using CACHE-FIRST approach for instant population");
            
            try
            {
                // PERFORMANCE FIX: Check cache FIRST and populate immediately if available
                if (this._areaIdToName.Any() && this._entityToAreaId.Any())
                {
                    PluginLog.Info($"{LogPrefix} Cache available - populating list IMMEDIATELY from {this._areaIdToName.Count} cached areas");
                    this.PopulateAreaListFromCache(e);
                    
                    // Trigger background refresh to update cache (fire and forget)
                    PluginLog.Info($"{LogPrefix} Starting background refresh to update cache");
                    _ = Task.Run(async () => await this.RefreshAreaCacheAsync(e));
                    return;
                }

                // No cache available - must do full load (first time or after error)
                PluginLog.Info($"{LogPrefix} No cache available - performing full load for initial population");
                this.PerformFullAreaLoad(e);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} Area list population failed with cache-first approach");
                e.AddItem("!error", "Error loading areas", ex.Message);
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"Error loading areas: {ex.Message}");
            }
        }

        /// <summary>
        /// Populates the area list immediately from cached data for instant UI response.
        /// </summary>
        /// <param name="e">Event arguments containing dropdown information.</param>
        private void PopulateAreaListFromCache(ActionEditorListboxItemsRequestedEventArgs e)
        {
            try
            {
                // Get areas that have lights (from entity->area cache)
                var areasWithLights = this._entityToAreaId.Values
                    .Where(areaId => !String.IsNullOrEmpty(areaId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Order by area name
                var orderedAreas = areasWithLights
                    .Select(aid => (aid, name: this._areaIdToName.TryGetValue(aid, out var n) ? n : aid))
                    .OrderBy(t => t.name, StringComparer.CurrentCultureIgnoreCase);

                var count = 0;
                foreach (var (areaId, areaName) in orderedAreas)
                {
                    // Count lights in this area from cache
                    var lightCount = this._entityToAreaId.Values.Count(aid =>
                        String.Equals(aid, areaId, StringComparison.OrdinalIgnoreCase));
                    
                    var displayName = $"{areaName} ({lightCount} light{(lightCount == 1 ? "" : "s")})";
                    e.AddItem(name: areaId, displayName: displayName, description: $"Area with {lightCount} lights");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} INSTANT population from cache: {count} area(s)");
                
                // Keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlArea) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Keeping current selection from cache: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} Failed to populate from cache");
                // Fall back to full load if cache population fails
                this.PerformFullAreaLoad(e);
            }
        }

        /// <summary>
        /// Performs background refresh of area cache and updates the UI when complete.
        /// </summary>
        /// <param name="e">Event arguments for updating the list when refresh completes.</param>
        private async Task RefreshAreaCacheAsync(ActionEditorListboxItemsRequestedEventArgs e)
        {
            try
            {
                PluginLog.Info($"{LogPrefix} Background refresh: Starting cache update");
                
                // Perform full data fetch in background
                if (!await this.EnsureHaReadyAsync())
                {
                    PluginLog.Warning($"{LogPrefix} Background refresh: EnsureHaReady failed");
                    return;
                }

                if (this._dataService == null)
                {
                    PluginLog.Error($"{LogPrefix} Background refresh: DataService not available");
                    return;
                }

                // Fetch all required data
                var (ok, json, error) = await this._dataService.FetchStatesAsync(CancellationToken.None);
                if (!ok || String.IsNullOrEmpty(json))
                {
                    PluginLog.Warning($"{LogPrefix} Background refresh: Failed to fetch states - {error}");
                    return;
                }

                // Initialize LightStateManager
                if (this._lightStateManager != null && this._dataParser != null)
                {
                    var (success, errorMessage) = await this._lightStateManager.InitOrUpdateAsync(this._dataService, this._dataParser, CancellationToken.None);
                    if (!success)
                    {
                        PluginLog.Warning($"{LogPrefix} Background refresh: LightStateManager update failed - {errorMessage}");
                        return;
                    }
                }

                // Fetch registry data
                var (okEnt, entJson, errEnt) = await this._dataService.FetchEntityRegistryAsync(CancellationToken.None);
                var (okDev, devJson, errDev) = await this._dataService.FetchDeviceRegistryAsync(CancellationToken.None);
                var (okArea, areaJson, errArea) = await this._dataService.FetchAreaRegistryAsync(CancellationToken.None);

                // Parse data
                var registryData = this._dataParser.ParseRegistries(devJson, entJson, areaJson);
                var lights = this._dataParser.ParseLightStates(json, registryData);

                // Update caches
                this.UpdateInternalCaches(lights, registryData);

                PluginLog.Info($"{LogPrefix} Background refresh: Cache updated with {lights.Count()} lights in {this._areaIdToName.Count} areas - ready for next dropdown open");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} Background refresh failed");
            }
        }

        /// <summary>
        /// Performs full area load when no cache is available (initial load or after errors).
        /// </summary>
        /// <param name="e">Event arguments containing dropdown information.</param>
        private void PerformFullAreaLoad(ActionEditorListboxItemsRequestedEventArgs e)
        {
            try
            {
                // STEP 1: Ensure HA connection
                if (!this.EnsureHaReadyAsync().GetAwaiter().GetResult())
                {
                    PluginLog.Warning($"{LogPrefix} Full load: EnsureHaReady failed (not connected/authenticated)");
                    if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var _) ||
                        !this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var _))
                    {
                        e.AddItem("!not_configured", "Home Assistant not configured", "Open plugin settings");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Home Assistant URL and Token not configured");
                    }
                    else
                    {
                        e.AddItem("!not_connected", "Could not connect to Home Assistant", "Check URL/token");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Could not connect to Home Assistant");
                    }
                    return;
                }

                // STEP 2: Check DataService availability
                if (this._dataService == null)
                {
                    PluginLog.Error($"{LogPrefix} Full load: DataService not available");
                    e.AddItem("!no_service", "Data service not available", "Plugin initialization error");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Plugin initialization error");
                    return;
                }

                // STEP 3: Fetch states
                PluginLog.Info($"{LogPrefix} Full load: Fetching states using modern service architecture");
                var (ok, json, error) = this._dataService.FetchStatesAsync(CancellationToken.None).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} Full load: FetchStatesAsync ok={ok} error='{error}' bytes={json?.Length ?? 0}");

                if (!ok || String.IsNullOrEmpty(json))
                {
                    e.AddItem("!no_states", $"Failed to fetch states: {error ?? "unknown"}", "Check connection");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"Failed to fetch entity states error: {error}");
                    return;
                }

                // STEP 4: Initialize LightStateManager
                if (this._lightStateManager != null && this._dataService != null && this._dataParser != null)
                {
                    var (success, errorMessage) = this._lightStateManager.InitOrUpdateAsync(this._dataService, this._dataParser, CancellationToken.None).GetAwaiter().GetResult();
                    if (!success)
                    {
                        PluginLog.Warning($"{LogPrefix} Full load: LightStateManager.InitOrUpdateAsync failed: {errorMessage}");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"Failed to load light data: {errorMessage}");
                        e.AddItem("!init_failed", "Failed to load lights", errorMessage ?? "Check connection to Home Assistant");
                        return;
                    }
                }

                // STEP 5: Fetch registry data
                PluginLog.Info($"{LogPrefix} Full load: Fetching registry data for area information");
                var (okEnt, entJson, errEnt) = this._dataService.FetchEntityRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();
                var (okDev, devJson, errDev) = this._dataService.FetchDeviceRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();
                var (okArea, areaJson, errArea) = this._dataService.FetchAreaRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();

                // STEP 6: Parse data
                var registryData = this._dataParser.ParseRegistries(devJson, entJson, areaJson);
                var lights = this._dataParser.ParseLightStates(json, registryData);

                // STEP 7: Update caches
                this.UpdateInternalCaches(lights, registryData);

                // STEP 8: Populate list from fresh data
                var areaIds = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                foreach (var light in lights)
                {
                    if (!String.IsNullOrEmpty(light.AreaId))
                    {
                        areaIds.Add(light.AreaId);
                    }
                }

                var orderedAreas = areaIds
                    .Select(aid => (aid, name: this._areaIdToName.TryGetValue(aid, out var n) ? n : aid))
                    .OrderBy(t => t.name, StringComparer.CurrentCultureIgnoreCase);

                var count = 0;
                foreach (var (areaId, areaName) in orderedAreas)
                {
                    var lightsInArea = lights.Where(l => String.Equals(l.AreaId, areaId, StringComparison.OrdinalIgnoreCase));
                    var lightCount = lightsInArea.Count();
                    
                    var displayName = $"{areaName} ({lightCount} light{(lightCount == 1 ? "" : "s")})";
                    e.AddItem(name: areaId, displayName: displayName, description: $"Area with {lightCount} lights");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} Full load: List populated with {count} area(s)");
                
                if (count > 0)
                {
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal, $"Successfully loaded {count} areas with lights");
                }
                else
                {
                    e.AddItem("!no_areas", "No areas with lights found", "Check Home Assistant configuration");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Warning, "No areas with lights found");
                }
                
                // Keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlArea) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Full load: Keeping current selection: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} Full load failed");
                e.AddItem("!error", "Error loading areas", ex.Message);
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, $"Error loading areas: {ex.Message}");
            }
        }


        /// <summary>
        /// Updates internal caches from services - follows the successful HomeAssistantLightsDynamicFolder pattern.
        /// Key difference: Extracts areas from lights data, not from registry-only data.
        /// </summary>
        /// <param name="lights">Light data containing area assignments.</param>
        /// <param name="registryData">Registry data for area names.</param>
        private void UpdateInternalCaches(IEnumerable<LightData> lights, ParsedRegistryData registryData)
        {
            // Clear existing UI data (following HomeAssistantLightsDynamicFolder pattern)
            this._entityToAreaId.Clear();
            this._areaIdToName.Clear();

            PluginLog.Debug($"{LogPrefix} UpdateInternalCaches: Clearing area caches, following successful HomeAssistantLightsDynamicFolder pattern");

            // Update from registry data - area ID to name mapping
            foreach (var (areaId, areaName) in registryData.AreaIdToName)
            {
                this._areaIdToName[areaId] = areaName;
            }

            // CRITICAL: Extract areas FROM lights data (the key working pattern)
            foreach (var light in lights)
            {
                // Map entity to area (this is where areas come from - lights data, not registry!)
                this._entityToAreaId[light.EntityId] = light.AreaId;
            }

            var lightCount = lights.Count();
            var areaCount = registryData.AreaIdToName.Count;
            PluginLog.Info($"{LogPrefix} Updated internal caches: {lightCount} lights, {areaCount} areas - areas extracted from lights data");
        }
    }
}
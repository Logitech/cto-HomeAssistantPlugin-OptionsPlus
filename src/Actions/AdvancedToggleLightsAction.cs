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
    /// Advanced action for toggling multiple Home Assistant lights with comprehensive control options.
    /// Supports brightness, color temperature, hue/saturation, and white level adjustments across RGB, RGBW, and RGBWW light types.
    /// Uses modern dependency injection pattern with debounced light control for optimal performance.
    /// </summary>
    public sealed class AdvancedToggleLightsAction : ActionEditorCommand, IDisposable
    {
        /// <summary>
        /// Logging prefix for this action's log messages.
        /// </summary>
        private const String LogPrefix = "[AdvancedToggleLights]";

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
        /// Registry service for device, entity, and area management.
        /// </summary>
        private IRegistryService? _registryService;

        /// <summary>
        /// Capability service for analyzing light feature support.
        /// </summary>
        private readonly CapabilityService _capSvc = new();

        /// <summary>
        /// Indicates whether this instance has been disposed.
        /// </summary>
        private Boolean _disposed = false;

        /// <summary>
        /// Simple toggle state for all selected lights. Defaults to false (off state).
        /// This boolean alternates between true/false on each press, and all lights get the same command.
        /// </summary>
        private Boolean _advancedLightsState = false;

        /// <summary>
        /// Control name for primary light selection dropdown.
        /// </summary>
        private const String ControlLights = "ha_lights";

        /// <summary>
        /// Control name for additional lights text input (comma-separated entity IDs).
        /// </summary>
        private const String ControlAdditionalLights = "ha_additional_lights";

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
        /// Cache TTL in minutes for registry data to prevent refetching on every dropdown open.
        /// </summary>
        private const Int32 CacheTtlMinutes = 5;

        /// <summary>
        /// Icon service for rendering action button graphics.
        /// </summary>
        private readonly IconService _icons;

        /// <summary>
        /// Cache timestamp for registry data to implement basic TTL.
        /// </summary>
        private DateTime _cacheTimestamp = DateTime.MinValue;

        /// <summary>
        /// Cached lights list to prevent registry refetching on every dropdown open.
        /// </summary>
        private List<LightData>? _cachedLights = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="AdvancedToggleLightsAction"/> class.
        /// Sets up action editor controls for light selection and parameter configuration.
        /// </summary>
        public AdvancedToggleLightsAction()
        {
            this.Name = "HomeAssistant.AdvancedToggleLights";
            this.DisplayName = "Advanced Toggle Lights";
            this.GroupName = "Lights";
            this.Description = "Toggle multiple Home Assistant lights with advanced controls for brightness, color, and temperature. For rgbw and rgbww lights for white levels to be sent hue and saturation must also be provided.";

            // Primary light selection (single)
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlLights, "Primary Light(retry if empty)"));

            // Additional lights (comma-separated entity IDs)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlAdditionalLights, "Additional Lights (comma-separated)")
                    .SetPlaceholder("light.living_room,light.kitchen")
            );

            // Brightness control (0-255)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlBrightness, "Brightness (0-255)")
                    .SetPlaceholder("128")
            );

            // Color temperature in Kelvin (2000-6500)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlTemperature, "Temperature (2000K-6500K)")
                    .SetPlaceholder("3000")
            );

            // Hue (0-360 degrees)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlHue, "Hue (0-360°)")
                    .SetPlaceholder("0")
            );

            // Saturation (0-100%)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlSaturation, "Saturation (0-100%)")
                    .SetPlaceholder("100")
            );

            // White level (0-255) - for RGBW lights or warm white in RGBWW
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlWhiteLevel, "White Level / Warm White (0-255)")
                    .SetPlaceholder("255")
            );

            // Cold white level (0-255) - for RGBWW lights only
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlColdWhiteLevel, "Cold White Level (0-255)")
                    .SetPlaceholder("255")
            );

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Bulb, "light_bulb_icon.svg" }
            });

            PluginLog.Info($"{LogPrefix} Constructor completed - dependency initialization deferred to OnLoad()");
        }

        /// <summary>
        /// Gets the command image for the action button.
        /// </summary>
        /// <param name="parameters">Action editor parameters.</param>
        /// <param name="width">Requested image width.</param>
        /// <param name="height">Requested image height.</param>
        /// <returns>Bitmap image showing a light bulb icon.</returns>
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height) =>
            // Always show bulb icon for now
            this._icons.Get(IconId.Bulb);

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

                    this._registryService = new RegistryService();

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
                this._registryService = null;

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
        /// Executes the advanced toggle lights command with comprehensive light control.
        /// Processes multiple lights with brightness, color temperature, hue/saturation, and white level parameters.
        /// Implements toggle behavior - if lights are on with parameters, turns them off; if off, turns on with parameters.
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

                // Get selected lights
                var selectedLights = new List<String>();

                // Add primary light
                if (ps.TryGetString(ControlLights, out var primaryLight) && !String.IsNullOrWhiteSpace(primaryLight))
                {
                    selectedLights.Add(primaryLight.Trim());
                }

                // Add additional lights
                if (ps.TryGetString(ControlAdditionalLights, out var additionalLights) && !String.IsNullOrWhiteSpace(additionalLights))
                {
                    var additionalList = additionalLights.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !String.IsNullOrEmpty(s) && !selectedLights.Contains(s, StringComparer.OrdinalIgnoreCase));

                    selectedLights.AddRange(additionalList);
                }

                if (!selectedLights.Any())
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No lights selected");
                    return false;
                }

                PluginLog.Info($"{LogPrefix} Press: Processing {selectedLights.Count} lights with individual capabilities");

                // Parse control values using defined constants
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, MaxBrightness);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, MinTemperature, MaxTemperature);
                var hue = this.ParseDoubleParameter(ps, ControlHue, MinHue, MaxHue);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, MinSaturation, MaxSaturation);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, MaxBrightness);
                var coldWhiteLevel = this.ParseIntParameter(ps, ControlColdWhiteLevel, 0, MaxBrightness);

                // Process lights with individual capability filtering (NEW APPROACH)
                var success = this.ProcessLightsIndividually(selectedLights, brightness, temperature, hue, saturation, whiteLevel, coldWhiteLevel);

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
        /// Processes multiple lights with individual capability filtering.
        /// Key difference from common capabilities: each light uses its own maximum supported features.
        /// Follows the successful AreaToggleLightsAction.ProcessAreaLights() pattern.
        /// </summary>
        /// <param name="entityIds">Collection of light entity IDs to process.</param>
        /// <param name="brightness">Brightness value (0-255) or null.</param>
        /// <param name="temperature">Color temperature in Kelvin or null.</param>
        /// <param name="hue">Hue value (0-360) or null.</param>
        /// <param name="saturation">Saturation value (0-100) or null.</param>
        /// <param name="whiteLevel">White level (0-255) or null.</param>
        /// <param name="coldWhiteLevel">Cold white level (0-255) or null.</param>
        /// <returns><c>true</c> if all lights processed successfully; otherwise, <c>false</c>.</returns>
        private Boolean ProcessLightsIndividually(IEnumerable<String> entityIds, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel, Int32? coldWhiteLevel)
        {
            // Toggle the advanced lights state before processing lights - all lights get the same command
            this._advancedLightsState = !this._advancedLightsState;
            PluginLog.Info($"{LogPrefix} Toggled advanced lights state to: {(this._advancedLightsState ? "ON" : "OFF")}");

            var success = true;

            foreach (var entityId in entityIds)
            {
                try
                {
                    // Get INDIVIDUAL capabilities for this specific light (key change!)
                    var individualCaps = this._lightStateManager?.GetCapabilities(entityId)
                        ?? new LightCaps(true, false, false, false, null);

                    PluginLog.Info($"{LogPrefix} Processing {entityId} with individual capabilities: OnOff={individualCaps.OnOff}, Brightness={individualCaps.Brightness}, ColorTemp={individualCaps.ColorTemp}, ColorHs={individualCaps.ColorHs}");

                    // Process this light with ITS OWN capabilities (not intersection)
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

        private Boolean ProcessSingleLight(String entityId, LightCaps caps, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel, Int32? coldWhiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");

            // Use the individual capabilities passed in (no longer need fallback since caps are already individual)
            var preferredColorMode = caps.PreferredColorMode ?? "hs";

            PluginLog.Info($"{LogPrefix} Light capabilities: onoff={caps.OnOff} brightness={caps.Brightness} colorTemp={caps.ColorTemp} colorHs={caps.ColorHs} preferredColorMode={preferredColorMode}");
            PluginLog.Info($"{LogPrefix} Input parameters: brightness={brightness} temperature={temperature}K hue={hue}° saturation={saturation}% whiteLevel={whiteLevel} coldWhiteLevel={coldWhiteLevel}");

            if (this._lightSvc == null)
            {
                PluginLog.Error($"{LogPrefix} ProcessSingleLight: LightControlService not available");
                return false;
            }

            // Always use advanced lights state to determine on/off, regardless of parameters
            PluginLog.Info($"{LogPrefix} Using advanced lights state to determine command: {(this._advancedLightsState ? "ON" : "OFF")}");

            // Use simple advanced lights toggle state - all lights get the same command
            if (!this._advancedLightsState)
            {
                PluginLog.Info($"{LogPrefix} Advanced lights state is OFF, turning OFF light {entityId}");
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

            // Advanced lights state is ON, turn light ON with the specified parameters
            PluginLog.Info($"{LogPrefix} Advanced lights state is ON, turning ON light {entityId} with parameters");

            // Build service call data based on available parameters and capabilities
            var serviceData = new Dictionary<String, Object>();

            // Add brightness if supported and specified
            if (brightness.HasValue && caps.Brightness)
            {
                var bri = HSBHelper.Clamp(brightness.Value, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added brightness: {brightness.Value} -> {bri} (clamped {MinBrightness}-{MaxBrightness})");
            }
            else if ((whiteLevel.HasValue || coldWhiteLevel.HasValue) && caps.Brightness &&
                     !preferredColorMode.EqualsNoCase("rgbw") && !preferredColorMode.EqualsNoCase("rgbww"))
            {
                // White level as fallback brightness (only for non-RGBW/RGBWW lights)
                var whiteValue = whiteLevel ?? coldWhiteLevel ?? 0;
                var bri = HSBHelper.Clamp(whiteValue, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added white level as brightness fallback: {whiteValue} -> {bri} (clamped {MinBrightness}-{MaxBrightness})");
            }
            else if (brightness.HasValue && !caps.Brightness)
            {
                PluginLog.Warning($"{LogPrefix} Brightness {brightness.Value} requested but not supported by {entityId}");
            }
            else if ((whiteLevel.HasValue || coldWhiteLevel.HasValue) &&
                     (preferredColorMode.EqualsNoCase("rgbw") || preferredColorMode.EqualsNoCase("rgbww")))
            {
                var whiteLevels = new List<String>();
                if (whiteLevel.HasValue)
                {
                    whiteLevels.Add($"warm={whiteLevel.Value}");
                }

                if (coldWhiteLevel.HasValue)
                {
                    whiteLevels.Add($"cold={coldWhiteLevel.Value}");
                }

                PluginLog.Info($"{LogPrefix} White levels ({String.Join(", ", whiteLevels)}) will be used for white channels in {preferredColorMode} mode (not as brightness)");
            }

            // Add color controls - prioritize temperature over HS to avoid conflicts
            // (HA doesn't allow both color_temp and hs_color in the same call)
            if (temperature.HasValue && caps.ColorTemp)
            {
                var kelvin = HSBHelper.Clamp(temperature.Value, MinTemperature, MaxTemperature);
                var mired = ColorTemp.KelvinToMired(kelvin);
                serviceData["color_temp"] = mired;
                PluginLog.Info($"{LogPrefix} Added color temp: {temperature.Value}K -> {kelvin}K -> {mired} mireds (color temp takes priority over HS)");

                if (hue.HasValue || saturation.HasValue)
                {
                    PluginLog.Info($"{LogPrefix} Skipping HS color because color temperature was specified (HA doesn't allow both)");
                }
            }
            else if (hue.HasValue && saturation.HasValue && caps.ColorHs)
            {
                var h = HSBHelper.Wrap360(hue.Value);
                var s = HSBHelper.Clamp(saturation.Value, MinSaturation, MaxSaturation);

                // Use the preferred color mode for this light
                PluginLog.Info($"{LogPrefix} Using preferred color mode: {preferredColorMode}");

                switch (preferredColorMode.ToLowerInvariant())
                {
                    case "rgbww":
                        // Convert HS to RGBWW (R,G,B,ColdWhite,WarmWhite)
                        var (r1, g1, b1) = HSBHelper.HsbToRgb(h, s, FullColorValue);

                        // Use separate cold and warm white levels if specified
                        var coldWhite = 0;
                        var warmWhite = 0;

                        // Priority 1: Use separate cold/warm white levels if specified
                        if (coldWhiteLevel.HasValue || whiteLevel.HasValue)
                        {
                            if (coldWhiteLevel.HasValue && whiteLevel.HasValue)
                            {
                                // Both specified - use them directly
                                coldWhite = HSBHelper.Clamp(coldWhiteLevel.Value, 0, MaxBrightness);
                                warmWhite = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                                PluginLog.Info($"{LogPrefix} Using separate white levels: cold={coldWhite}, warm={warmWhite}");
                            }
                            else if (coldWhiteLevel.HasValue)
                            {
                                // Only cold white specified - use it for both channels
                                coldWhite = HSBHelper.Clamp(coldWhiteLevel.Value, 0, MaxBrightness);
                                warmWhite = coldWhite;
                                PluginLog.Info($"{LogPrefix} Only cold white specified: using {coldWhite} for both channels");
                            }
                            else if (whiteLevel.HasValue)
                            {
                                // Only warm white specified - use it for both channels
                                warmWhite = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                                coldWhite = warmWhite;
                                PluginLog.Info($"{LogPrefix} Only warm white specified: using {warmWhite} for both channels");
                            }
                        }
                        // Priority 2: Use temperature-based distribution (legacy behavior)
                        else if (whiteLevel.HasValue && temperature.HasValue)
                        {
                            var white = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                            var kelvin = HSBHelper.Clamp(temperature.Value, MinTemperature, MaxTemperature);
                            // Convert temperature to cold/warm ratio (2000K = warm, 6500K = cold)
                            var tempRatio = (kelvin - MinTemperature) / (Double)(MaxTemperature - MinTemperature);
                            coldWhite = (Int32)(white * tempRatio);
                            warmWhite = (Int32)(white * (1.0 - tempRatio));
                            PluginLog.Info($"{LogPrefix} Using temperature-based distribution: {kelvin}K -> cold={coldWhite}, warm={warmWhite}");
                        }

                        serviceData["rgbww_color"] = new Object[] { r1, g1, b1, coldWhite, warmWhite };
                        PluginLog.Info($"{LogPrefix} Added rgbww_color: HS({h:F1}°,{s:F1}%) -> RGBWW({r1},{g1},{b1},{coldWhite},{warmWhite})");
                        break;

                    case "rgbw":
                        // Convert HS to RGBW (R,G,B,White)
                        var (r2, g2, b2) = HSBHelper.HsbToRgb(h, s, FullColorValue);

                        // Use whiteLevel for white channel if specified
                        var whiteChannel = 0;
                        if (whiteLevel.HasValue)
                        {
                            whiteChannel = HSBHelper.Clamp(whiteLevel.Value, 0, MaxBrightness);
                        }

                        serviceData["rgbw_color"] = new Object[] { r2, g2, b2, whiteChannel };
                        PluginLog.Info($"{LogPrefix} Added rgbw_color: HS({h:F1}°,{s:F1}%) + white({whiteLevel}) -> RGBW({r2},{g2},{b2},{whiteChannel})");
                        break;

                    case "rgb":
                        // Convert HS to RGB
                        var (r3, g3, b3) = HSBHelper.HsbToRgb(h, s, FullColorValue);
                        serviceData["rgb_color"] = new Object[] { r3, g3, b3 };
                        PluginLog.Info($"{LogPrefix} Added rgb_color: HS({h:F1}°,{s:F1}%) -> RGB({r3},{g3},{b3})");
                        break;

                    case "hs":
                    default:
                        // Use HS color (force decimal serialization while keeping in valid ranges)
                        var hueForJson = Math.Abs(h % 1.0) < 0.001 ?
                            (h >= 359.9 ? h - 0.0001 : h + 0.0001) : h;
                        var satForJson = Math.Abs(s % 1.0) < 0.001 ?
                            (s >= 99.9 ? s - 0.0001 : s + 0.0001) : s;
                        serviceData["hs_color"] = new Object[] { hueForJson, satForJson };
                        PluginLog.Info($"{LogPrefix} Added hs_color: HS({h:F1}°,{s:F1}%) -> HS({hueForJson:F4}°,{satForJson:F4}%)");
                        break;
                }
            }
            else if (temperature.HasValue && !caps.ColorTemp)
            {
                PluginLog.Warning($"{LogPrefix} Color temperature {temperature.Value}K requested but not supported by {entityId}");
            }
            else if ((hue.HasValue || saturation.HasValue) && !caps.ColorHs)
            {
                PluginLog.Warning($"{LogPrefix} Hue/Saturation requested but not supported by {entityId}");
            }

            // FIXED: Send separate requests for better compatibility with WiZ and other lights
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

                PluginLog.Info($"{LogPrefix} Separated into {brightnessData.Count} brightness attrs, {colorData.Count} color attrs, {tempData.Count} temp attrs");

                // 1. First call: Turn on with brightness (most compatible)
                if (brightnessData.Any())
                {
                    var briData = JsonSerializer.SerializeToElement(brightnessData);
                    var briJson = JsonSerializer.Serialize(briData);
                    PluginLog.Info($"{LogPrefix} CALL 1/3: Brightness - {briJson}");

                    var briSuccess = this._lightSvc.TurnOnAsync(entityId, briData).GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} HA SERVICE CALL 1: turn_on entity_id={entityId} data={briJson} -> success={briSuccess}");
                    overallSuccess &= briSuccess;

                    if (briSuccess)
                    {
                        // Small delay to ensure the light processes the brightness before color
                        Thread.Sleep(50);
                    }
                }
                else
                {
                    // Turn on without parameters first
                    PluginLog.Info($"{LogPrefix} CALL 1/3: Simple turn_on (no brightness specified)");
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
                    PluginLog.Info($"{LogPrefix} CALL 2/3: Temperature - {tempJson}");

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
                    PluginLog.Info($"{LogPrefix} CALL 3/3: Color - {colorJson}");

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

        private void OnListboxItemsRequested(Object? sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlLights))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName}) using modern service architecture");
            try
            {


                // Ensure we're connected before asking HA for states
                if (!this.EnsureHaReadyAsync().GetAwaiter().GetResult())
                {
                    PluginLog.Warning($"{LogPrefix} List: EnsureHaReady failed (not connected/authenticated)");
                    if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var _) ||
                        !this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var _))
                    {
                        e.AddItem("!not_configured", "Home Assistant not configured", "Open plugin settings");
                        // Report configuration error to user
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                            "Home Assistant URL and Token not configured");
                    }
                    else
                    {
                        e.AddItem("!not_connected", "Could not connect to Home Assistant", "Check URL/token");
                        // Report connection error to user
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                            "Could not connect to Home Assistant");
                    }
                    return;
                }

                if (this._dataService == null)
                {
                    PluginLog.Error($"{LogPrefix} ListboxItemsRequested: DataService not available");
                    e.AddItem("!no_service", "Data service not available", "Plugin initialization error");
                    // Report service error to user
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        "Plugin initialization error");
                    return;
                }

                // Check cache first to avoid refetching registry data on every dropdown open
                var now = DateTime.Now;
                var cacheExpired = (now - this._cacheTimestamp).TotalMinutes > CacheTtlMinutes;

                List<LightData> lights;
                if (this._cachedLights != null && !cacheExpired)
                {
                    PluginLog.Info($"{LogPrefix} Using cached lights data ({this._cachedLights.Count} lights, age: {(now - this._cacheTimestamp).TotalMinutes:F1}min)");
                    lights = this._cachedLights;
                }
                else
                {
                    PluginLog.Info($"{LogPrefix} Cache expired or empty, fetching fresh registry-aware data");

                    // Fetch states using modern data service
                    var (ok, json, error) = this._dataService.FetchStatesAsync(CancellationToken.None)
                        .GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} FetchStatesAsync ok={ok} error='{error}' bytes={json?.Length ?? 0}");

                    if (!ok || String.IsNullOrEmpty(json))
                    {
                        e.AddItem("!no_states", $"Failed to fetch states: {error ?? "unknown"}", "Check connection");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                            $"Failed to fetch entity states error: {error}");
                        return;
                    }

                    // Initialize LightStateManager using self-contained method
                    if (this._lightStateManager != null && this._dataService != null && this._dataParser != null)
                    {
                        var (success, errorMessage) = this._lightStateManager.InitOrUpdateAsync(this._dataService, this._dataParser, CancellationToken.None).GetAwaiter().GetResult();
                        if (!success)
                        {
                            PluginLog.Warning($"{LogPrefix} LightStateManager.InitOrUpdateAsync failed: {errorMessage}");
                            this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                                $"Failed to load light data: {errorMessage}");
                            e.AddItem("!init_failed", "Failed to load lights", errorMessage ?? "Check connection to Home Assistant");
                            return;
                        }
                    }

                    // FIXED: Use registry-aware parsing instead of direct JSON parsing
                    PluginLog.Info($"{LogPrefix} Fetching registry data for registry-aware light parsing");

                    // Ensure _dataService is not null before using it
                    if (this._dataService == null)
                    {
                        PluginLog.Error($"{LogPrefix} DataService is null when trying to fetch registry data");
                        e.AddItem("!no_service", "Data service not available", "Plugin initialization error");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Data service not available");
                        return;
                    }

                    var (entSuccess, entJson, _) = this._dataService.FetchEntityRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();
                    var (devSuccess, devJson, _) = this._dataService.FetchDeviceRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();
                    var (areaSuccess, areaJson, _) = this._dataService.FetchAreaRegistryAsync(CancellationToken.None).GetAwaiter().GetResult();

                    // Ensure _dataParser is not null before using it
                    if (this._dataParser == null)
                    {
                        PluginLog.Error($"{LogPrefix} DataParser is null when trying to parse registry data");
                        e.AddItem("!no_parser", "Data parser not available", "Plugin initialization error");
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Data parser not available");
                        return;
                    }

                    // Parse registry data and light states together (working AreaToggleLights pattern)
                    var registryData = this._dataParser.ParseRegistries(devJson, entJson, areaJson);
                    lights = this._dataParser.ParseLightStates(json, registryData);

                    // Cache the results
                    this._cachedLights = lights;
                    this._cacheTimestamp = now;
                    PluginLog.Info($"{LogPrefix} Cached {lights.Count} lights with registry data (TTL: {CacheTtlMinutes}min)");
                }

                // Iterate over parsed lights instead of raw JSON elements
                var count = 0;
                foreach (var light in lights)
                {
                    var display = !String.IsNullOrEmpty(light.FriendlyName)
                        ? $"{light.FriendlyName} ({light.EntityId})"
                        : light.EntityId;

                    e.AddItem(name: light.EntityId, displayName: display, description: "Home Assistant light");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} List populated with {count} light(s) using modern service architecture");

                // Clear any previous error status since we successfully loaded lights
                if (count > 0)
                {
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal,
                        $"Successfully loaded {count} lights");
                }

                // Keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlLights) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Keeping current selection: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading lights", ex.Message);
            }
        }
    }
}
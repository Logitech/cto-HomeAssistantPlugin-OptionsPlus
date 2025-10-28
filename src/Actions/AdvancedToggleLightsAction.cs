namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;
    using Loupedeck.HomeAssistantPlugin.Services;

    public sealed class AdvancedToggleLightsAction : ActionEditorCommand, IDisposable
    {
        private const String LogPrefix = "[AdvancedToggleLights]";

        // Service dependencies - modern dependency injection pattern
        private IHaClient? _ha;
        private ILightControlService? _lightSvc;
        private ILightStateManager? _lightStateManager;
        private IHomeAssistantDataService? _dataService;
        private IHomeAssistantDataParser? _dataParser;
        private IRegistryService? _registryService;

        private readonly CapabilityService _capSvc = new();
        private Boolean _disposed = false;

        // Control names
        private const String ControlLights = "ha_lights";
        private const String ControlAdditionalLights = "ha_additional_lights";
        private const String ControlBrightness = "ha_brightness";
        private const String ControlTemperature = "ha_temperature";
        private const String ControlHue = "ha_hue";
        private const String ControlSaturation = "ha_saturation";
        private const String ControlWhiteLevel = "ha_white_level";
        private const String ControlColdWhiteLevel = "ha_cold_white_level";

        // Constants - extracted and organized like the newer code
        private const Int32 MinBrightness = 1;
        private const Int32 MaxBrightness = 255;
        private const Int32 MinTemperature = 2000;
        private const Int32 MaxTemperature = 6500;
        private const Double MinHue = 0.0;
        private const Double MaxHue = 360.0;
        private const Double MinSaturation = 0.0;
        private const Double MaxSaturation = 100.0;
        private const Double FullColorValue = 100.0;
        private const Int32 AuthTimeoutSeconds = 8;
        private const Int32 DebounceMs = 100;

        private readonly IconService _icons;

        public AdvancedToggleLightsAction()
        {
            this.Name = "HomeAssistant.AdvancedToggleLights";
            this.DisplayName = "Advanced Toggle Lights";
            this.GroupName = "Lights";
            this.Description = "Toggle multiple Home Assistant lights with advanced controls for brightness, color, and temperature.";

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

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            // Always show bulb icon for now
            return this._icons.Get(IconId.Bulb);
        }

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

        protected override Boolean OnUnload()
        {
            PluginLog.Info($"{LogPrefix} OnUnload()");
            this.Dispose();
            return true;
        }

        public void Dispose()
        {
            if (this._disposed)
                return;

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

        // Ensure we have an authenticated connection using modern service architecture
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

        private LightCaps GetLightCapabilities(JsonElement attrs)
        {
            return this._capSvc.ForLight(attrs);
        }

        private LightCaps GetCommonCapabilities(IEnumerable<String> entityIds)
        {
            if (!entityIds.Any())
                return new LightCaps(false, false, false, false, null);

            if (this._lightStateManager == null)
            {
                PluginLog.Warning($"{LogPrefix} GetCommonCapabilities: LightStateManager not available, using default caps");
                return new LightCaps(true, false, false, false, null);
            }

            var allCaps = new List<LightCaps>();

            foreach (var entityId in entityIds)
            {
                var caps = this._lightStateManager.GetCapabilities(entityId);
                allCaps.Add(caps);
            }

            if (!allCaps.Any())
                return new LightCaps(true, false, false, false, null);

            // Return intersection of all capabilities (what ALL lights support)
            var commonOnOff = allCaps.All(c => c.OnOff);
            var commonBrightness = allCaps.All(c => c.Brightness);
            var commonColorTemp = allCaps.All(c => c.ColorTemp);
            var commonColorHs = allCaps.All(c => c.ColorHs);

            PluginLog.Debug($"{LogPrefix} Common capabilities for {entityIds.Count()} lights: OnOff={commonOnOff}, Brightness={commonBrightness}, ColorTemp={commonColorTemp}, ColorHs={commonColorHs}");
            return new LightCaps(commonOnOff, commonBrightness, commonColorTemp, commonColorHs, null);
        }

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

                PluginLog.Info($"{LogPrefix} Press: Processing {selectedLights.Count} lights");

                // Get common capabilities
                var commonCaps = this.GetCommonCapabilities(selectedLights);

                // Parse control values using defined constants
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, MaxBrightness);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, MinTemperature, MaxTemperature);
                var hue = this.ParseDoubleParameter(ps, ControlHue, MinHue, MaxHue);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, MinSaturation, MaxSaturation);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, MaxBrightness);
                var coldWhiteLevel = this.ParseIntParameter(ps, ControlColdWhiteLevel, 0, MaxBrightness);

                // Process each light
                var success = true;
                foreach (var entityId in selectedLights)
                {
                    try
                    {
                        success &= this.ProcessSingleLight(entityId, commonCaps, brightness, temperature, hue, saturation, whiteLevel, coldWhiteLevel);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, $"{LogPrefix} Failed to process light {entityId}");
                        success = false;
                    }
                }

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

        private Int32? ParseIntParameter(ActionEditorActionParameters ps, String controlName, Int32 min, Int32 max)
        {
            if (!ps.TryGetString(controlName, out var valueStr) || String.IsNullOrWhiteSpace(valueStr))
                return null;

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

        private Double? ParseDoubleParameter(ActionEditorActionParameters ps, String controlName, Double min, Double max)
        {
            if (!ps.TryGetString(controlName, out var valueStr) || String.IsNullOrWhiteSpace(valueStr))
                return null;

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

        private Boolean ProcessSingleLight(String entityId, LightCaps caps, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel, Int32? coldWhiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");

            // Get individual light capabilities for preferred color mode
            var individualCaps = this._lightStateManager?.GetCapabilities(entityId) ?? caps;
            var preferredColorMode = individualCaps.PreferredColorMode ?? "hs";

            PluginLog.Info($"{LogPrefix} Light capabilities: onoff={caps.OnOff} brightness={caps.Brightness} colorTemp={caps.ColorTemp} colorHs={caps.ColorHs} preferredColorMode={preferredColorMode}");
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
                        $"Failed to toggle light {friendlyName}",
                        "Check Home Assistant logs for details");
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
                        $"Failed to turn off light {friendlyName}",
                        "Check Home Assistant logs for details");
                }

                return success;
            }

            // Light is OFF, turn it ON with the specified parameters
            PluginLog.Info($"{LogPrefix} Light {entityId} is OFF, turning ON with parameters");

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
                if (whiteLevel.HasValue) whiteLevels.Add($"warm={whiteLevel.Value}");
                if (coldWhiteLevel.HasValue) whiteLevels.Add($"cold={coldWhiteLevel.Value}");
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
                            var tempRatio = (kelvin - MinTemperature) / (double)(MaxTemperature - MinTemperature);
                            coldWhite = (int)(white * tempRatio);
                            warmWhite = (int)(white * (1.0 - tempRatio));
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
                        $"Failed to control light {friendlyName}",
                        "Check Home Assistant logs for details");
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
                        $"Failed to turn on light {friendlyName}",
                        "Check Home Assistant logs for details");
                }

                return success;
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
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
                            "Home Assistant not configured",
                            "Please set Base URL and Token in plugin settings");
                    }
                    else
                    {
                        e.AddItem("!not_connected", "Could not connect to Home Assistant", "Check URL/token");
                        // Report connection error to user
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                            "Could not connect to Home Assistant",
                            "Please check your Base URL and Token settings");
                    }
                    return;
                }

                if (this._dataService == null)
                {
                    PluginLog.Error($"{LogPrefix} ListboxItemsRequested: DataService not available");
                    e.AddItem("!no_service", "Data service not available", "Plugin initialization error");
                    // Report service error to user
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        "Plugin initialization error",
                        "Data service not available - please restart plugin");
                    return;
                }

                // Use modern data service instead of direct client calls
                var (ok, json, error) = this._dataService.FetchStatesAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} FetchStatesAsync ok={ok} error='{error}' bytes={json?.Length ?? 0}");

                if (!ok || String.IsNullOrEmpty(json))
                {
                    e.AddItem("!no_states", $"Failed to fetch states: {error ?? "unknown"}", "Check connection");
                    Plugin.OnPluginStatusChanged(PluginStatus.Error,
                        $"Failed to fetch entity states error: {error}");
                    return;
                }

                // Initialize LightStateManager using self-contained method
                // This fixes the bug where light states are unknown when first launching the plugin
                if (this._lightStateManager != null && this._dataService != null && this._dataParser != null)
                {
                    var (success, errorMessage) = this._lightStateManager.InitOrUpdateAsync(this._dataService, this._dataParser, CancellationToken.None).GetAwaiter().GetResult();
                    if (!success)
                    {
                        PluginLog.Warning($"{LogPrefix} LightStateManager.InitOrUpdateAsync failed: {errorMessage}");
                        // Report initialization error to user
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                            "Failed to load light data",
                            errorMessage ?? "Unknown error occurred while fetching lights from Home Assistant");
                        // Still show the dropdown with error message
                        e.AddItem("!init_failed", "Failed to load lights", errorMessage ?? "Check connection to Home Assistant");
                        return;
                    }
                }

                // The loading indicator will be replaced by actual items
                var count = 0;
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("entity_id", out var idProp))
                    {
                        continue;
                    }

                    var id = idProp.GetString();
                    if (String.IsNullOrEmpty(id) || !id.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var display = id;
                    if (el.TryGetProperty("attributes", out var attrs) &&
                        attrs.ValueKind == JsonValueKind.Object &&
                        attrs.TryGetProperty("friendly_name", out var fn) &&
                        fn.ValueKind == JsonValueKind.String)
                    {
                        display = $"{fn.GetString()} ({id})";
                    }

                    e.AddItem(name: id, displayName: display, description: "Home Assistant light");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} List populated with {count} light(s) using modern service architecture");

                // Clear any previous error status since we successfully loaded lights
                if (count > 0)
                {
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Normal,
                        $"Successfully loaded {count} lights",
                        null);
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

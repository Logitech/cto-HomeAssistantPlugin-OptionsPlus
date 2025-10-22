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

    public sealed class AdvancedToggleAreaLightsAction : ActionEditorCommand, IDisposable
    {
        private const String LogPrefix = "[AdvancedToggleAreaLights]";
        
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
        private const String ControlArea = "ha_area";
        private const String ControlBrightness = "ha_brightness";
        private const String ControlTemperature = "ha_temperature";
        private const String ControlHue = "ha_hue";
        private const String ControlSaturation = "ha_saturation";
        private const String ControlWhiteLevel = "ha_white_level";

        // Constants - extracted and organized like the newer code
        private const Int32 MinBrightness = 1;
        private const Int32 MaxBrightness = 255;
        private const Int32 MinTemperature = 2000;
        private const Int32 MaxTemperature = 6500;
        private const Double MinHue = 0.0;
        private const Double MaxHue = 360.0;
        private const Double MinSaturation = 0.0;
        private const Double MaxSaturation = 100.0;
        private const Int32 AuthTimeoutSeconds = 8;
        private const Int32 DebounceMs = 100;

        // Area constants (matching HomeAssistantLightsDynamicFolder)
        private const String UnassignedAreaId = "!unassigned";
        private const String UnassignedAreaName = "(No area)";

        private readonly IconService _icons;

        public AdvancedToggleAreaLightsAction()
        {
            this.Name = "HomeAssistant.AdvancedToggleAreaLights";
            this.DisplayName = "Advanced Toggle Area Lights";
            this.GroupName = "Lights";
            this.Description = "Toggle all lights in a Home Assistant area with advanced controls for brightness, color, and temperature.";

            // Area selection (single)
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlArea, "Area"));

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

            // White level (0-255)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlWhiteLevel, "White Level (0-255)")
                    .SetPlaceholder("255")
            );

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Area, "area_icon.svg" }
            });

            PluginLog.Info($"{LogPrefix} Constructor completed - dependency initialization deferred to OnLoad()");
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            // Show area icon
            return this._icons.Get(IconId.Area);
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

        private List<String> GetAreaLights(String areaId)
        {
            PluginLog.Info($"{LogPrefix} Getting lights for area: {areaId} using modern service architecture");

            if (this._dataService == null || this._registryService == null)
            {
                PluginLog.Error($"{LogPrefix} GetAreaLights: Required services not available");
                return new List<String>();
            }

            try
            {
                // Use modern data service to fetch all required data
                var (okStates, statesJson, errStates) = this._dataService.FetchStatesAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (!okStates || String.IsNullOrEmpty(statesJson))
                {
                    PluginLog.Warning($"{LogPrefix} FetchStatesAsync failed: {errStates}");
                    return new List<String>();
                }

                var (okEnt, entJson, errEnt) = this._dataService.FetchEntityRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();

                var (okDev, devJson, errDev) = this._dataService.FetchDeviceRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();

                var (okArea, areaJson, errArea) = this._dataService.FetchAreaRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Parse registry data using modern parser
                if (this._dataParser == null)
                {
                    PluginLog.Error($"{LogPrefix} GetAreaLights: DataParser not available");
                    return new List<String>();
                }

                var registryData = this._dataParser.ParseRegistries(devJson, entJson, areaJson);
                this._registryService.UpdateRegistries(registryData);

                // Find all light entity IDs first
                var allLightIds = new List<String>();
                using var statesDoc = JsonDocument.Parse(statesJson);
                foreach (var state in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = state.GetPropertyOrDefault("entity_id");
                    if (!String.IsNullOrEmpty(entityId) && entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        allLightIds.Add(entityId);
                    }
                }

                // Use registry service to get lights in the specified area
                var areaLights = this._registryService.GetLightsInArea(areaId, allLightIds).ToList();

                PluginLog.Info($"{LogPrefix} Found {areaLights.Count} lights in area '{areaId}' using modern services: {String.Join(", ", areaLights)}");
                return areaLights;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} GetAreaLights failed");
                return new List<String>();
            }
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

                // Get selected area
                if (!ps.TryGetString(ControlArea, out var selectedArea) || String.IsNullOrWhiteSpace(selectedArea))
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No area selected");
                    return false;
                }

                selectedArea = selectedArea.Trim();
                PluginLog.Info($"{LogPrefix} Processing area: {selectedArea}");

                // Get lights in the area
                var areaLights = this.GetAreaLights(selectedArea);

                if (!areaLights.Any())
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No lights found in area '{selectedArea}'");
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Warning,
                        $"No lights found in area '{selectedArea}'",
                        "Check that the area exists and has lights assigned");
                    return false;
                }

                PluginLog.Info($"{LogPrefix} Press: Processing {areaLights.Count} lights in area '{selectedArea}'");

                // Parse control values using defined constants
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, MaxBrightness);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, MinTemperature, MaxTemperature);
                var hue = this.ParseDoubleParameter(ps, ControlHue, MinHue, MaxHue);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, MinSaturation, MaxSaturation);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, MaxBrightness);

                // Process each light in the area
                var success = true;
                foreach (var entityId in areaLights)
                {
                    try
                    {
                        success &= this.ProcessSingleLight(entityId, brightness, temperature, hue, saturation, whiteLevel);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(ex, $"{LogPrefix} Failed to process light {entityId}");
                        success = false;
                    }
                }

                var message = success 
                    ? $"Successfully controlled {areaLights.Count} lights in area '{selectedArea}'"
                    : $"Some lights in area '{selectedArea}' failed to respond";

                this.Plugin.OnPluginStatusChanged(success ? PluginStatus.Normal : PluginStatus.Warning,
                    message, success ? null : "Check Home Assistant logs for details");

                PluginLog.Info($"{LogPrefix} RunCommand completed with success={success}");
                return success;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} RunCommand exception");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error,
                    "Failed to control area lights", ex.Message);
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

        private Boolean ProcessSingleLight(String entityId, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");
            PluginLog.Info($"{LogPrefix} Input parameters: brightness={brightness} temperature={temperature}K hue={hue}° saturation={saturation}% whiteLevel={whiteLevel}");

            if (this._lightSvc == null)
            {
                PluginLog.Error($"{LogPrefix} ProcessSingleLight: LightControlService not available");
                return false;
            }

            // If no parameters specified, just toggle
            if (!brightness.HasValue && !temperature.HasValue && !hue.HasValue && !saturation.HasValue && !whiteLevel.HasValue)
            {
                PluginLog.Info($"{LogPrefix} No parameters provided, using simple toggle for '{entityId}'");
                var success = this._lightSvc.ToggleAsync(entityId).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: toggle entity_id={entityId} -> success={success}");
                
                if (!success)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to toggle light {entityId}");
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
                
                if (!success)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to turn off light {entityId}");
                }
                
                return success;
            }

            // Light is OFF, turn it ON with the specified parameters
            PluginLog.Info($"{LogPrefix} Light {entityId} is OFF, turning ON with parameters");

            // Build service call data - send all requested parameters and let HA ignore unsupported ones
            var serviceData = new Dictionary<String, Object>();

            // Add brightness if specified
            if (brightness.HasValue)
            {
                var bri = HSBHelper.Clamp(brightness.Value, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added brightness: {brightness.Value} -> {bri} (clamped {MinBrightness}-{MaxBrightness})");
            }
            else if (whiteLevel.HasValue)
            {
                // White level as fallback brightness
                var bri = HSBHelper.Clamp(whiteLevel.Value, MinBrightness, MaxBrightness);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added white level as brightness: {whiteLevel.Value} -> {bri} (clamped {MinBrightness}-{MaxBrightness})");
            }

            // Add color controls - prioritize temperature over HS to avoid conflicts
            // (HA doesn't allow both color_temp and hs_color in the same call)
            if (temperature.HasValue)
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
            else if (hue.HasValue && saturation.HasValue)
            {
                var h = HSBHelper.Wrap360(hue.Value);
                var s = HSBHelper.Clamp(saturation.Value, MinSaturation, MaxSaturation);
                serviceData["hs_color"] = new Double[] { h, s };
                PluginLog.Info($"{LogPrefix} Added hs_color: hue {hue.Value}° -> {h}°, saturation {saturation.Value}% -> {s}%");
            }

            JsonElement? data = null;
            if (serviceData.Any())
            {
                data = JsonSerializer.SerializeToElement(serviceData);
                var dataJson = JsonSerializer.Serialize(data);
                PluginLog.Info($"{LogPrefix} Built service data: {dataJson}");
                
                // Use turn_on when we have specific parameters
                var success = this._lightSvc.TurnOnAsync(entityId, data).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: turn_on entity_id={entityId} data={dataJson} -> success={success}");
                
                if (!success)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to control light {entityId}");
                }
                
                return success;
            }
            else
            {
                // No valid parameters, just turn on without parameters
                PluginLog.Info($"{LogPrefix} No valid parameters, turning ON without specific parameters for '{entityId}'");
                var success = this._lightSvc.TurnOnAsync(entityId).GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: turn_on entity_id={entityId} data=null -> success={success}");
                
                if (!success)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to turn on light {entityId}");
                }
                
                return success;
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlArea))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName}) using modern service architecture");
            try
            {
                // Ensure we're connected before asking HA for areas
                if (!this.EnsureHaReadyAsync().GetAwaiter().GetResult())
                {
                    PluginLog.Warning($"{LogPrefix} List: EnsureHaReady failed (not connected/authenticated)");
                    if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var _) ||
                        !this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var _))
                    {
                        e.AddItem("!not_configured", "Home Assistant not configured", "Open plugin settings");
                    }
                    else
                    {
                        e.AddItem("!not_connected", "Could not connect to Home Assistant", "Check URL/token");
                    }
                    return;
                }

                if (this._dataService == null || this._dataParser == null || this._registryService == null)
                {
                    PluginLog.Error($"{LogPrefix} ListboxItemsRequested: Required services not available");
                    e.AddItem("!no_service", "Data services not initialized", "Plugin initialization error");
                    return;
                }

                // Use modern data service to fetch all required data
                var (okAreas, areasJson, errAreas) = this._dataService.FetchAreaRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okStates, statesJson, errStates) = this._dataService.FetchStatesAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okEnt, entJson, errEnt) = this._dataService.FetchEntityRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okDev, devJson, errDev) = this._dataService.FetchDeviceRegistryAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();

                PluginLog.Info($"{LogPrefix} Modern service calls: areas={okAreas} states={okStates} entities={okEnt} devices={okDev}");

                if (!okAreas || !okStates || String.IsNullOrEmpty(areasJson) || String.IsNullOrEmpty(statesJson))
                {
                    e.AddItem("!no_data", $"Failed to fetch data: {errAreas ?? errStates ?? "unknown"}", "Check connection");
                    return;
                }

                // Parse registry data using modern parser
                var registryData = this._dataParser.ParseRegistries(devJson, entJson, areasJson);
                this._registryService.UpdateRegistries(registryData);

                // Find all light entity IDs first
                var allLightIds = new List<String>();
                using var statesDoc = JsonDocument.Parse(statesJson);
                foreach (var state in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = state.GetPropertyOrDefault("entity_id");
                    if (!String.IsNullOrEmpty(entityId) && entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    {
                        allLightIds.Add(entityId);
                    }
                }

                // Get areas that have lights using modern registry service
                var areasWithLights = this._registryService.GetAreasWithLights(allLightIds).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Build area_id -> name mapping from registry data
                var areaIdToName = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
                foreach (var areaId in this._registryService.GetAllAreaIds())
                {
                    var areaName = this._registryService.GetAreaName(areaId);
                    if (!String.IsNullOrEmpty(areaName))
                    {
                        areaIdToName[areaId] = areaName;
                    }
                }

                // Add unassigned area if there are unassigned lights
                if (areasWithLights.Contains(UnassignedAreaId))
                {
                    areaIdToName[UnassignedAreaId] = UnassignedAreaName;
                }

                // Sort areas by name and add to dropdown
                var sortedAreas = areasWithLights
                    .Select(areaId => new { Id = areaId, Name = areaIdToName.TryGetValue(areaId, out var name) ? name : areaId })
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var count = 0;
                foreach (var area in sortedAreas)
                {
                    e.AddItem(name: area.Id, displayName: area.Name, description: "Home Assistant area with lights");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} List populated with {count} area(s) that have lights using modern service architecture");

                // Keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlArea) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Keeping current selection: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading areas", ex.Message);
            }
        }
    }
}
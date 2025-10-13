namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class AdvancedToggleAreaLightsAction : ActionEditorCommand
    {
        private const String LogPrefix = "[AdvancedToggleAreaLights]";
        private HaWebSocketClient _client;
        private LightControlService _lightSvc;
        private readonly CapabilityService _capSvc = new();

        // Control names
        private const String ControlArea = "ha_area";
        private const String ControlBrightness = "ha_brightness";
        private const String ControlTemperature = "ha_temperature";
        private const String ControlHue = "ha_hue";
        private const String ControlSaturation = "ha_saturation";
        private const String ControlWhiteLevel = "ha_white_level";

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
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            // Show area icon
            return this._icons.Get(IconId.Area);
        }

        protected override Boolean OnLoad()
        {
            PluginLog.Info($"{LogPrefix} OnLoad()");
            if (this.Plugin is HomeAssistantPlugin p)
            {
                this._client = p.HaClient;
                
                // Initialize light control service with reasonable debounce times
                var ha = new HaClientAdapter(this._client);
                this._lightSvc = new LightControlService(ha, 100, 100, 100);
                
                return true;
            }
            PluginLog.Warning($"{LogPrefix} OnLoad(): plugin not available");
            return false;
        }

        // Ensure we have an authenticated WS
        private async Task<Boolean> EnsureHaReadyAsync()
        {
            if (this._client?.IsAuthenticated == true)
            {
                PluginLog.Info($"{LogPrefix} EnsureHaReady: already authenticated");
                return true;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) ||
                String.IsNullOrWhiteSpace(baseUrl))
            {
                PluginLog.Warning($"{LogPrefix} EnsureHaReady: Missing ha.baseUrl setting");
                return false;
            }

            if (!this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingToken, out var token) ||
                String.IsNullOrWhiteSpace(token))
            {
                PluginLog.Warning($"{LogPrefix} EnsureHaReady: Missing ha.token setting");
                return false;
            }

            try
            {
                PluginLog.Info($"{LogPrefix} Connecting to HA… url='{baseUrl}'");
                var (ok, msg) = await this._client.ConnectAndAuthenticateAsync(
                    baseUrl, token, TimeSpan.FromSeconds(8), CancellationToken.None
                ).ConfigureAwait(false);

                PluginLog.Info($"{LogPrefix} Auth result ok={ok} msg='{msg}'");
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} EnsureHaReady exception");
                return false;
            }
        }

        private LightCaps GetLightCapabilities(JsonElement attrs)
        {
            return this._capSvc.ForLight(attrs);
        }

        private List<String> GetAreaLights(String areaId)
        {
            PluginLog.Info($"{LogPrefix} Getting lights for area: {areaId}");

            // Get states to find lights
            var (okStates, statesJson, errStates) = this._client.RequestAsync("get_states", CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!okStates || String.IsNullOrEmpty(statesJson))
            {
                PluginLog.Warning($"{LogPrefix} get_states failed: {errStates}");
                return new List<String>();
            }

            // Get entity registry to map entities to areas
            var (okEnt, entJson, errEnt) = this._client.RequestAsync("config/entity_registry/list", CancellationToken.None)
                .GetAwaiter().GetResult();

            // Get device registry for device->area mapping
            var (okDev, devJson, errDev) = this._client.RequestAsync("config/device_registry/list", CancellationToken.None)
                .GetAwaiter().GetResult();

            // Build entity->area and device->area mappings
            var entityToArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            var entityToDevice = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            var deviceToArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

            if (okEnt && !String.IsNullOrEmpty(entJson))
            {
                using var entDoc = JsonDocument.Parse(entJson);
                if (entDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ent in entDoc.RootElement.EnumerateArray())
                    {
                        var entityId = ent.GetPropertyOrDefault("entity_id");
                        if (String.IsNullOrEmpty(entityId)) continue;

                        var deviceId = ent.GetPropertyOrDefault("device_id") ?? "";
                        var entAreaId = ent.GetPropertyOrDefault("area_id");

                        if (!String.IsNullOrEmpty(deviceId))
                        {
                            entityToDevice[entityId] = deviceId;
                        }

                        if (!String.IsNullOrEmpty(entAreaId))
                        {
                            entityToArea[entityId] = entAreaId;
                        }
                    }
                }
            }

            if (okDev && !String.IsNullOrEmpty(devJson))
            {
                using var devDoc = JsonDocument.Parse(devJson);
                if (devDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dev in devDoc.RootElement.EnumerateArray())
                    {
                        var deviceId = dev.GetPropertyOrDefault("id");
                        var devAreaId = dev.GetPropertyOrDefault("area_id");

                        if (!String.IsNullOrEmpty(deviceId) && !String.IsNullOrEmpty(devAreaId))
                        {
                            deviceToArea[deviceId] = devAreaId;
                        }
                    }
                }
            }

            // Find lights in the specified area
            var areaLights = new List<String>();

            using var statesDoc = JsonDocument.Parse(statesJson);
            foreach (var state in statesDoc.RootElement.EnumerateArray())
            {
                var entityId = state.GetPropertyOrDefault("entity_id");
                if (String.IsNullOrEmpty(entityId) || !entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Determine this light's area (entity area wins, then device area, then unassigned)
                String lightAreaId = null;
                
                if (entityToArea.TryGetValue(entityId, out var entArea))
                {
                    lightAreaId = entArea;
                }
                else if (entityToDevice.TryGetValue(entityId, out var deviceId) &&
                         deviceToArea.TryGetValue(deviceId, out var devArea))
                {
                    lightAreaId = devArea;
                }

                if (String.IsNullOrEmpty(lightAreaId))
                {
                    lightAreaId = UnassignedAreaId;
                }

                // Check if this light belongs to our target area
                if (!String.Equals(lightAreaId, areaId, StringComparison.OrdinalIgnoreCase))
                    continue;

                areaLights.Add(entityId);
            }

            PluginLog.Info($"{LogPrefix} Found {areaLights.Count} lights in area '{areaId}': {String.Join(", ", areaLights)}");

            return areaLights;
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

                // Parse control values
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, 255);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, 2000, 6500);
                var hue = this.ParseDoubleParameter(ps, ControlHue, 0, 360);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, 0, 100);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, 255);

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
                return HSBHelper.Clamp(value, min, max);
            }

            return null;
        }

        private Double? ParseDoubleParameter(ActionEditorActionParameters ps, String controlName, Double min, Double max)
        {
            if (!ps.TryGetString(controlName, out var valueStr) || String.IsNullOrWhiteSpace(valueStr))
                return null;

            if (Double.TryParse(valueStr, out var value))
            {
                return HSBHelper.Clamp(value, min, max);
            }

            return null;
        }

        private Boolean ProcessSingleLight(String entityId, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");
            PluginLog.Info($"{LogPrefix} Input parameters: brightness={brightness} temperature={temperature}K hue={hue}° saturation={saturation}% whiteLevel={whiteLevel}");

            // If no parameters specified, just toggle
            if (!brightness.HasValue && !temperature.HasValue && !hue.HasValue && !saturation.HasValue && !whiteLevel.HasValue)
            {
                PluginLog.Info($"{LogPrefix} No parameters provided, using simple toggle for '{entityId}'");
                var (ok, err) = this._client.CallServiceAsync("light", "toggle", entityId, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: domain=light service=toggle entity_id={entityId} data=null -> ok={ok} err='{err}'");
                
                if (!ok)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to toggle light {entityId}: {err ?? "Unknown error"}");
                }
                
                return ok;
            }

            // Build service call data - send all requested parameters and let HA ignore unsupported ones
            var serviceData = new Dictionary<String, Object>();

            // Add brightness if specified
            if (brightness.HasValue)
            {
                var bri = HSBHelper.Clamp(brightness.Value, 1, 255); // Ensure at least 1 for turn_on
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added brightness: {brightness.Value} -> {bri} (clamped 1-255)");
            }
            else if (whiteLevel.HasValue)
            {
                // White level as fallback brightness
                var bri = HSBHelper.Clamp(whiteLevel.Value, 1, 255);
                serviceData["brightness"] = bri;
                PluginLog.Info($"{LogPrefix} Added white level as brightness: {whiteLevel.Value} -> {bri} (clamped 1-255)");
            }

            // Add color controls - prioritize temperature over HS to avoid conflicts
            // (HA doesn't allow both color_temp and hs_color in the same call)
            if (temperature.HasValue)
            {
                var kelvin = HSBHelper.Clamp(temperature.Value, 2000, 6500);
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
                var s = HSBHelper.Clamp(saturation.Value, 0, 100);
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
                var (ok, err) = this._client.CallServiceAsync("light", "turn_on", entityId, data, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: domain=light service=turn_on entity_id={entityId} data={dataJson} -> ok={ok} err='{err}'");
                
                if (!ok)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to control light {entityId}: {err ?? "Unknown error"}");
                }
                
                return ok;
            }
            else
            {
                // No parameters, just toggle
                PluginLog.Info($"{LogPrefix} No parameters provided, using simple toggle for '{entityId}'");
                var (ok, err) = this._client.CallServiceAsync("light", "toggle", entityId, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} HA SERVICE CALL: domain=light service=toggle entity_id={entityId} data=null -> ok={ok} err='{err}'");
                
                if (!ok)
                {
                    PluginLog.Warning($"{LogPrefix} Failed to toggle light {entityId}: {err ?? "Unknown error"}");
                }
                
                return ok;
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlArea))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName})");
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

                // Get areas, states, entity registry, and device registry
                var (okAreas, areasJson, errAreas) = this._client.RequestAsync("config/area_registry/list", CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okStates, statesJson, errStates) = this._client.RequestAsync("get_states", CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okEnt, entJson, errEnt) = this._client.RequestAsync("config/entity_registry/list", CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                var (okDev, devJson, errDev) = this._client.RequestAsync("config/device_registry/list", CancellationToken.None)
                    .GetAwaiter().GetResult();

                PluginLog.Info($"{LogPrefix} Registry calls: areas={okAreas} states={okStates} entities={okEnt} devices={okDev}");

                if (!okAreas || !okStates || String.IsNullOrEmpty(areasJson) || String.IsNullOrEmpty(statesJson))
                {
                    e.AddItem("!no_data", $"Failed to fetch data: {errAreas ?? errStates ?? "unknown"}", "Check connection");
                    return;
                }

                // Build area_id -> name mapping
                var areaIdToName = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
                using var areasDoc = JsonDocument.Parse(areasJson);
                if (areasDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var area in areasDoc.RootElement.EnumerateArray())
                    {
                        var id = area.GetPropertyOrDefault("area_id") ?? area.GetPropertyOrDefault("id");
                        var name = area.GetPropertyOrDefault("name") ?? id ?? "";
                        if (!String.IsNullOrEmpty(id))
                        {
                            areaIdToName[id] = name;
                        }
                    }
                }

                // Build entity->area and device->area mappings
                var entityToArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
                var entityToDevice = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
                var deviceToArea = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);

                if (okEnt && !String.IsNullOrEmpty(entJson))
                {
                    using var entDoc = JsonDocument.Parse(entJson);
                    if (entDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var ent in entDoc.RootElement.EnumerateArray())
                        {
                            var entityId = ent.GetPropertyOrDefault("entity_id");
                            if (String.IsNullOrEmpty(entityId)) continue;

                            var deviceId = ent.GetPropertyOrDefault("device_id") ?? "";
                            var entAreaId = ent.GetPropertyOrDefault("area_id");

                            if (!String.IsNullOrEmpty(deviceId))
                            {
                                entityToDevice[entityId] = deviceId;
                            }

                            if (!String.IsNullOrEmpty(entAreaId))
                            {
                                entityToArea[entityId] = entAreaId;
                            }
                        }
                    }
                }

                if (okDev && !String.IsNullOrEmpty(devJson))
                {
                    using var devDoc = JsonDocument.Parse(devJson);
                    if (devDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dev in devDoc.RootElement.EnumerateArray())
                        {
                            var deviceId = dev.GetPropertyOrDefault("id");
                            var devAreaId = dev.GetPropertyOrDefault("area_id");

                            if (!String.IsNullOrEmpty(deviceId) && !String.IsNullOrEmpty(devAreaId))
                            {
                                deviceToArea[deviceId] = devAreaId;
                            }
                        }
                    }
                }

                // Find which areas actually have lights
                var areasWithLights = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
                using var statesDoc = JsonDocument.Parse(statesJson);
                foreach (var state in statesDoc.RootElement.EnumerateArray())
                {
                    var entityId = state.GetPropertyOrDefault("entity_id");
                    if (String.IsNullOrEmpty(entityId) || !entityId.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Determine this light's area
                    String lightAreaId = null;
                    
                    if (entityToArea.TryGetValue(entityId, out var entArea))
                    {
                        lightAreaId = entArea;
                    }
                    else if (entityToDevice.TryGetValue(entityId, out var deviceId) && 
                             deviceToArea.TryGetValue(deviceId, out var devArea))
                    {
                        lightAreaId = devArea;
                    }

                    if (String.IsNullOrEmpty(lightAreaId))
                    {
                        lightAreaId = UnassignedAreaId;
                    }

                    areasWithLights.Add(lightAreaId);
                }

                // Add unassigned area name if needed
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

                PluginLog.Info($"{LogPrefix} List populated with {count} area(s) that have lights");

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
                PluginLog.Warning(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading areas", ex.Message);
            }
        }
    }
}
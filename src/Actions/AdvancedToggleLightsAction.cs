namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class AdvancedToggleLightsAction : ActionEditorCommand
    {
        private const String LogPrefix = "[AdvancedToggleLights]";
        private HaWebSocketClient _client;
        private LightControlService _lightSvc;
        private readonly CapabilityService _capSvc = new();

        // Control names
        private const String ControlLights = "ha_lights";
        private const String ControlAdditionalLights = "ha_additional_lights";
        private const String ControlBrightness = "ha_brightness";
        private const String ControlTemperature = "ha_temperature";
        private const String ControlHue = "ha_hue";
        private const String ControlSaturation = "ha_saturation";
        private const String ControlWhiteLevel = "ha_white_level";

        private readonly IconService _icons;

        public AdvancedToggleLightsAction()
        {
            this.Name = "HomeAssistant.AdvancedToggleLights";
            this.DisplayName = "Advanced Toggle Lights";
            this.GroupName = "Lights";
            this.Description = "Toggle multiple Home Assistant lights with advanced controls for brightness, color, and temperature.";

            // Primary light selection (single)
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlLights, "Primary Light"));

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

            // White level (0-255)
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlWhiteLevel, "White Level (0-255)")
                    .SetPlaceholder("255")
            );

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Bulb, "light_bulb_icon.svg" }
            });
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            // Always show bulb icon for now
            return this._icons.Get(IconId.Bulb);
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

        private LightCaps GetCommonCapabilities(IEnumerable<String> entityIds)
        {
            if (!entityIds.Any())
                return new LightCaps(false, false, false, false);

            // Get states to determine capabilities
            var (ok, json, error) = this._client.RequestAsync("get_states", CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!ok || String.IsNullOrEmpty(json))
                return new LightCaps(true, false, false, false); // Default fallback

            var allCaps = new List<LightCaps>();
            
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("entity_id", out var idProp))
                    continue;

                var id = idProp.GetString();
                if (String.IsNullOrEmpty(id) || !entityIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (el.TryGetProperty("attributes", out var attrs))
                {
                    var caps = this.GetLightCapabilities(attrs);
                    allCaps.Add(caps);
                }
            }

            if (!allCaps.Any())
                return new LightCaps(true, false, false, false);

            // Return intersection of all capabilities (what ALL lights support)
            var commonOnOff = allCaps.All(c => c.OnOff);
            var commonBrightness = allCaps.All(c => c.Brightness);
            var commonColorTemp = allCaps.All(c => c.ColorTemp);
            var commonColorHs = allCaps.All(c => c.ColorHs);

            return new LightCaps(commonOnOff, commonBrightness, commonColorTemp, commonColorHs);
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

                // Parse control values
                var brightness = this.ParseIntParameter(ps, ControlBrightness, 0, 255);
                var temperature = this.ParseIntParameter(ps, ControlTemperature, 2000, 6500);
                var hue = this.ParseDoubleParameter(ps, ControlHue, 0, 360);
                var saturation = this.ParseDoubleParameter(ps, ControlSaturation, 0, 100);
                var whiteLevel = this.ParseIntParameter(ps, ControlWhiteLevel, 0, 255);

                // Process each light
                var success = true;
                foreach (var entityId in selectedLights)
                {
                    try
                    {
                        success &= this.ProcessSingleLight(entityId, commonCaps, brightness, temperature, hue, saturation, whiteLevel);
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

        private Boolean ProcessSingleLight(String entityId, LightCaps caps, Int32? brightness, Int32? temperature,
            Double? hue, Double? saturation, Int32? whiteLevel)
        {
            PluginLog.Info($"{LogPrefix} Processing light: {entityId}");

            // If no parameters specified, just toggle
            if (!brightness.HasValue && !temperature.HasValue && !hue.HasValue && !saturation.HasValue && !whiteLevel.HasValue)
            {
                var (ok, err) = this._client.CallServiceAsync("light", "toggle", entityId, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} call_service light.toggle '{entityId}' -> ok={ok} err='{err}'");
                return ok;
            }

            // Build service call data based on available parameters and capabilities
            var serviceData = new Dictionary<String, Object>();

            // Add brightness if supported and specified
            if (brightness.HasValue && caps.Brightness)
            {
                var bri = HSBHelper.Clamp(brightness.Value, 1, 255); // Ensure at least 1 for turn_on
                serviceData["brightness"] = bri;
            }
            else if (whiteLevel.HasValue && caps.Brightness)
            {
                // White level as fallback brightness
                var bri = HSBHelper.Clamp(whiteLevel.Value, 1, 255);
                serviceData["brightness"] = bri;
            }

            // Add color temperature if supported and specified (convert Kelvin to mireds)
            if (temperature.HasValue && caps.ColorTemp)
            {
                var kelvin = HSBHelper.Clamp(temperature.Value, 2000, 6500);
                var mired = ColorTemp.KelvinToMired(kelvin);
                serviceData["color_temp"] = mired;
            }

            // Add hue/saturation if supported and specified
            if (hue.HasValue && saturation.HasValue && caps.ColorHs)
            {
                var h = HSBHelper.Wrap360(hue.Value);
                var s = HSBHelper.Clamp(saturation.Value, 0, 100);
                serviceData["hs_color"] = new Double[] { h, s };
            }

            JsonElement? data = null;
            if (serviceData.Any())
            {
                data = JsonSerializer.SerializeToElement(serviceData);
                
                // Use turn_on when we have specific parameters (like the dynamic folder does)
                var (ok, err) = this._client.CallServiceAsync("light", "turn_on", entityId, data, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} call_service light.turn_on '{entityId}' with {serviceData.Count} params -> ok={ok} err='{err}'");
                return ok;
            }
            else
            {
                // No valid parameters, just toggle
                var (ok, err) = this._client.CallServiceAsync("light", "toggle", entityId, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} call_service light.toggle '{entityId}' -> ok={ok} err='{err}'");
                return ok;
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlLights))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName})");
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
                    }
                    else
                    {
                        e.AddItem("!not_connected", "Could not connect to Home Assistant", "Check URL/token");
                    }
                    return;
                }

                var (ok, json, error) = this._client.RequestAsync("get_states", CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} get_states ok={ok} error='{error}' bytes={json?.Length ?? 0}");

                if (!ok || String.IsNullOrEmpty(json))
                {
                    e.AddItem("!no_states", $"Failed to fetch states: {error ?? "unknown"}", "Check connection");
                    return;
                }

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

                PluginLog.Info($"{LogPrefix} List populated with {count} light(s)");

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
                PluginLog.Warning(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading lights", ex.Message);
            }
        }
    }
}
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class ToggleCoverAction : ActionEditorCommand
    {
        private const String LogPrefix = "[ToggleCover]";
        private HaWebSocketClient _client;

        private const String ControlCover = "ha_cover";
        private const String ControlToggleType = "ha_toggle_type";
        
        private const String ToggleTypeNormal = "normal";
        private const String ToggleTypeTilt = "tilt";
        
        private readonly IconService _icons;
        private readonly CapabilityService _capSvc = new();

        public ToggleCoverAction()
        {
            this.Name = "HomeAssistant.ToggleCover";
            this.DisplayName = "Toggle Cover";
            this.GroupName = "Covers";
            this.Description = "Toggle a Home Assistant cover or its tilt position.";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlCover, "Cover"));
            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlToggleType, "Toggle Type"));

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Cover, "light_bulb_icon.svg" },
                { IconId.Blind, "light_bulb_icon.svg" },
                { IconId.Curtain, "light_bulb_icon.svg" },
                { IconId.Shade, "light_bulb_icon.svg" },
                { IconId.Shutter, "light_bulb_icon.svg" },
                { IconId.Awning, "light_bulb_icon.svg" },
                { IconId.Garage, "light_bulb_icon.svg" },
                { IconId.Gate, "light_bulb_icon.svg" },
                { IconId.Door, "light_bulb_icon.svg" },
                { IconId.Window, "light_bulb_icon.svg" }
            });
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            if (parameters.TryGetString(ControlCover, out var entityId) && !String.IsNullOrWhiteSpace(entityId))
            {
                // Try to get the appropriate icon based on device class if we have cached capability info
                return this._icons.Get(IconId.Cover);
            }
            return this._icons.Get(IconId.Cover);
        }

        protected override Boolean OnLoad()
        {
            PluginLog.Info($"{LogPrefix} OnLoad()");
            if (this.Plugin is HomeAssistantPlugin p)
            {
                this._client = p.HaClient;
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

                if (!ps.TryGetString(ControlCover, out var entityId) || String.IsNullOrWhiteSpace(entityId))
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No cover selected");
                    return false;
                }

                if (!ps.TryGetString(ControlToggleType, out var toggleType) || String.IsNullOrWhiteSpace(toggleType))
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No toggle type selected");
                    return false;
                }

                PluginLog.Info($"{LogPrefix} Press: entity='{entityId}' toggleType='{toggleType}'");

                // Send appropriate toggle command based on type
                Boolean ok;
                String err;
                
                if (toggleType.EqualsNoCase(ToggleTypeTilt))
                {
                    // First try the native toggle_tilt service
                    (ok, err) = this._client.CallServiceAsync("cover", "toggle_tilt", entityId, data: null, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} call_service cover.toggle_tilt '{entityId}' -> ok={ok} err='{err}'");
                    
                    // If toggle_tilt fails, implement manual toggle logic using individual tilt operations
                    if (!ok)
                    {
                        PluginLog.Info($"{LogPrefix} toggle_tilt failed, attempting manual tilt toggle for '{entityId}'");
                        (ok, err) = this.ToggleTiltManually(entityId);
                        PluginLog.Info($"{LogPrefix} manual tilt toggle '{entityId}' -> ok={ok} err='{err}'");
                    }
                }
                else
                {
                    (ok, err) = this._client.CallServiceAsync("cover", "toggle", entityId, data: null, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} call_service cover.toggle '{entityId}' -> ok={ok} err='{err}'");
                }
                
                return ok;
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

        private (Boolean ok, String err) ToggleTiltManually(String entityId)
        {
            try
            {
                // Get current state to determine which tilt action to perform
                var (stateOk, json, stateErr) = this._client.RequestAsync("get_states", CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                if (!stateOk || String.IsNullOrEmpty(json))
                {
                    return (false, $"Failed to get states: {stateErr}");
                }

                // Find our cover in the states
                using var doc = JsonDocument.Parse(json);
                JsonElement coverState = default;
                Boolean foundCover = false;
                
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("entity_id", out var idProp) &&
                        String.Equals(idProp.GetString(), entityId, StringComparison.OrdinalIgnoreCase))
                    {
                        coverState = el;
                        foundCover = true;
                        break;
                    }
                }

                if (!foundCover)
                {
                    return (false, $"Cover {entityId} not found in states");
                }

                // Get current tilt position
                var currentTilt = 50; // Default to middle position if we can't determine
                if (coverState.TryGetProperty("attributes", out var attrs) &&
                    attrs.TryGetProperty("current_tilt_position", out var tiltProp) &&
                    tiltProp.ValueKind == JsonValueKind.Number)
                {
                    currentTilt = tiltProp.GetInt32();
                }

                // Determine toggle action: if tilt is > 50%, close it; otherwise open it
                String action = currentTilt > 50 ? "close_cover_tilt" : "open_cover_tilt";
                
                var (ok, err) = this._client.CallServiceAsync("cover", action, entityId, data: null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                
                return (ok, err);
            }
            catch (Exception ex)
            {
                return (false, $"Manual tilt toggle exception: {ex.Message}");
            }
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(ControlToggleType))
            {
                PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName}) - Toggle Type");
                
                // Always add both toggle options - let the user decide what their device supports
                e.AddItem(name: ToggleTypeNormal, displayName: "Normal Toggle", description: "Toggle cover open/close");
                e.AddItem(name: ToggleTypeTilt, displayName: "Tilt Toggle", description: "Toggle cover tilt position");
                
                // Keep current selection
                var currentType = e.ActionEditorState?.GetControlValue(ControlToggleType) as String;
                if (!String.IsNullOrEmpty(currentType))
                {
                    e.SetSelectedItemName(currentType);
                }
                
                return;
            }

            if (!e.ControlName.EqualsNoCase(ControlCover))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName}) - Cover");
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
                    if (String.IsNullOrEmpty(id) || !id.StartsWith("cover.", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var display = id;
                    String deviceClass = "";
                    
                    if (el.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
                    {
                        if (attrs.TryGetProperty("friendly_name", out var fn) && fn.ValueKind == JsonValueKind.String)
                        {
                            display = $"{fn.GetString()} ({id})";
                        }
                        
                        if (attrs.TryGetProperty("device_class", out var dc) && dc.ValueKind == JsonValueKind.String)
                        {
                            deviceClass = dc.GetString() ?? "";
                        }
                    }

                    var description = String.IsNullOrEmpty(deviceClass)
                        ? "Home Assistant cover"
                        : $"Home Assistant {deviceClass}";

                    e.AddItem(name: id, displayName: display, description: description);
                    count++;
                }

                PluginLog.Info($"{LogPrefix} List populated with {count} cover(s)");

                // keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlCover) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Keeping current selection: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading covers", ex.Message);
            }
        }

    }
}
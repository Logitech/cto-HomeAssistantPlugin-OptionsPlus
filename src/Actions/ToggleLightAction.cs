namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;
    using Loupedeck.HomeAssistantPlugin.Services;

    /// <summary>
    /// Simple action for toggling Home Assistant lights on/off.
    /// Provides basic light control functionality with automatic entity discovery.
    /// </summary>
    public sealed class ToggleLightAction : ActionEditorCommand
    {
        /// <summary>
        /// Timeout in seconds for Home Assistant authentication operations.
        /// </summary>
        private const Int32 ConnectionTimeoutSeconds = 8;

        /// <summary>
        /// Logging prefix for this action's log messages.
        /// </summary>
        private const String LogPrefix = "[ToggleLight]";
        
        /// <summary>
        /// Home Assistant WebSocket client for communication.
        /// </summary>
        private HaWebSocketClient? _client;

        /// <summary>
        /// Control name for the light selection dropdown.
        /// </summary>
        private const String ControlLight = "ha_light";
        
        /// <summary>
        /// Icon service for rendering action button graphics.
        /// </summary>
        private readonly IconService _icons;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToggleLightAction"/> class.
        /// Sets up the action editor controls for light selection.
        /// </summary>
        public ToggleLightAction()
        {
            this.Name = "HomeAssistant.ToggleLight";
            this.DisplayName = "Toggle Light";
            this.GroupName = "Lights";
            this.Description = "Toggle a Home Assistant light on/off.";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlLight, "Light"));

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;

            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.Bulb, "light_bulb_icon.svg" }
            });
        }

        /// <summary>
        /// Gets the command image for the action button.
        /// </summary>
        /// <param name="parameters">Action editor parameters.</param>
        /// <param name="width">Requested image width.</param>
        /// <param name="height">Requested image height.</param>
        /// <returns>Bitmap image showing a light bulb icon.</returns>
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            if (parameters.TryGetString(ControlLight, out var entityId) && !String.IsNullOrWhiteSpace(entityId))
            {
                // Optionally, show On/Off icon based on cached state (if available)
                // For simplicity, always show bulb icon
                return this._icons.Get(IconId.Bulb);
            }
            return this._icons.Get(IconId.Bulb);
        }

        /// <summary>
        /// Loads the action and initializes the Home Assistant WebSocket client.
        /// </summary>
        /// <returns><c>true</c> if initialization succeeded; otherwise, <c>false</c>.</returns>
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

        /// <summary>
        /// Ensures Home Assistant connection is established and authenticated.
        /// Validates configuration settings and attempts connection if not already authenticated.
        /// </summary>
        /// <returns><c>true</c> if connection is ready; otherwise, <c>false</c>.</returns>
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
                PluginLog.Debug(() => $"{LogPrefix} Connecting to HA… url='{baseUrl}'");
                var (ok, msg) = await this._client!.ConnectAndAuthenticateAsync(
                    baseUrl, token, TimeSpan.FromSeconds(ConnectionTimeoutSeconds), CancellationToken.None
                ).ConfigureAwait(false);

                PluginLog.Info(() => $"{LogPrefix} Auth result ok={ok} msg='{msg}'");
                return ok;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} EnsureHaReady exception");
                return false;
            }
        }

        /// <summary>
        /// Executes the toggle light command.
        /// Sends a toggle service call to Home Assistant for the selected light entity.
        /// </summary>
        /// <param name="ps">Action editor parameters containing the selected light entity ID.</param>
        /// <returns><c>true</c> if the toggle command succeeded; otherwise, <c>false</c>.</returns>
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

                if (!ps.TryGetString(ControlLight, out var entityId) || String.IsNullOrWhiteSpace(entityId))
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No light selected");
                    return false;
                }

                PluginLog.Debug(() => $"{LogPrefix} Press: entity='{entityId}'");

                // Send toggle command
                var (ok, err) = this._client!.CallServiceAsync("light", "toggle", entityId, data: null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info(() => $"{LogPrefix} call_service light.toggle '{entityId}' -> ok={ok} err='{err}'");
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

        /// <summary>
        /// Handles listbox items requested event to populate the light selection dropdown.
        /// Fetches available light entities from Home Assistant and displays them with friendly names.
        /// Provides error handling for connection issues and configuration problems.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Listbox items requested event arguments.</param>
        private void OnListboxItemsRequested(Object? sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlLight))
            {
                return;
            }

            PluginLog.Debug(() => $"{LogPrefix} ListboxItemsRequested({e.ControlName})");
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

                var (ok, json, error) = this._client!.RequestAsync("get_states", CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Debug(() => $"{LogPrefix} get_states ok={ok} error='{error}' bytes={json?.Length ?? 0}");

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

                PluginLog.Debug(() => $"{LogPrefix} List populated with {count} light(s)");

                // keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlLight) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Debug(() => $"{LogPrefix} Keeping current selection: '{current}'");
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
namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class ToggleLightAction : ActionEditorCommand
    {
        // ====================================================================
        // CONSTANTS - Toggle Light Action Configuration
        // ====================================================================

        // --- Connection Constants ---
        private const Int32 ConnectionTimeoutSeconds = 8;              // Timeout for Home Assistant authentication

        private const String LogPrefix = "[ToggleLight]";
        private HaWebSocketClient? _client;

        private const String ControlLight = "ha_light";
        private readonly IconService _icons;

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
                var (ok, msg) = await this._client!.ConnectAndAuthenticateAsync(
                    baseUrl, token, TimeSpan.FromSeconds(ConnectionTimeoutSeconds), CancellationToken.None
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

                // Make sure we’re online before doing anything
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

                PluginLog.Info($"{LogPrefix} Press: entity='{entityId}'");

                // Send toggle command
                var (ok, err) = this._client!.CallServiceAsync("light", "toggle", entityId, data: null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} call_service light.toggle '{entityId}' -> ok={ok} err='{err}'");
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

        private void OnListboxItemsRequested(Object? sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlLight))
            {
                return;
            }

            PluginLog.Info($"{LogPrefix} ListboxItemsRequested({e.ControlName})");
            try
            {
                // Ensure we’re connected before asking HA for states
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

                // keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlLight) as String;
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
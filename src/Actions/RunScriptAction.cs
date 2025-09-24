namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class RunScriptAction : ActionEditorCommand
    {
        private const String LogPrefix = "[RunScript]";

        private HaWebSocketClient _client;
        private HaEventListener   _events;

        private const String ControlScript    = "ha_script";
        private const String ControlVarsJson  = "ha_vars_json";
        private const String ControlUseToggle = "ha_use_toggle";

        // Cache "running" state of scripts (kept updated by HaEventListener)
        private static readonly ConcurrentDictionary<String, Boolean> _isRunningCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Gate to avoid concurrent connect races
        private static readonly SemaphoreSlim _haConnectGate = new(1, 1);

        public RunScriptAction()
        {
            this.Name        = "HomeAssistant.RunScript";
            this.DisplayName = "HA: Run Script";
            this.GroupName   = "Home Assistant";
            this.Description = "Run a Home Assistant script with optional variables; press again to stop if running.";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(ControlScript, "Script"));
            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(ControlVarsJson, "Variables (JSON)")
                    .SetPlaceholder("{\"minutes\":5,\"who\":\"guest\"}")
            );
            this.ActionEditor.AddControlEx(
                new ActionEditorCheckbox(ControlUseToggle, "Prefer script.toggle (no variables)")
                    .SetDefaultValue(false)
            );

            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height)
        {
            // Default icon
            return PluginResources.ReadImage("area_icon.png");
        }

        protected override Boolean OnLoad()
        {
            PluginLog.Info($"{LogPrefix} OnLoad()");
            if (this.Plugin is HomeAssistantPlugin p)
            {
                this._client = p.HaClient;
                this._events = p.HaEvents;

                if (this._events != null)
                {
                    this._events.ScriptRunningChanged += (entityId, isRunning) =>
                    {
                        PluginLog.Info($"{LogPrefix} Event: ScriptRunningChanged entity='{entityId}' running={isRunning}");
                        _isRunningCache[entityId] = isRunning;
                        //this.ActionImageChanged(); // resets user icon
                    };
                }
                return true;
            }

            PluginLog.Warning($"{LogPrefix} OnLoad(): plugin not available");
            return false;
        }

        // Ensure we have an authenticated WS and events subscription
        private async Task<Boolean> EnsureHaReadyAsync()
        {
            if (this._client?.IsAuthenticated == true)
            {
                // Keep logs chatty but not noisy
                PluginLog.Info($"{LogPrefix} EnsureHaReady: already authenticated");
                return true;
            }

            // Pull creds from plugin settings
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

            await _haConnectGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (this._client.IsAuthenticated)
                {
                    PluginLog.Info($"{LogPrefix} EnsureHaReady: authenticated after gate");
                    return true;
                }

                PluginLog.Info($"{LogPrefix} Connecting to HA… url='{baseUrl}'");
                var (ok, msg) = await this._client.ConnectAndAuthenticateAsync(
                    baseUrl, token, TimeSpan.FromSeconds(8), CancellationToken.None
                ).ConfigureAwait(false);

                PluginLog.Info($"{LogPrefix} Auth result ok={ok} msg='{msg}'");
                if (!ok)
                {
                    PluginLog.Warning($"{LogPrefix} Authentication failed: {msg}");
                    return false;
                }

                // Subscribe to events (script running updates, etc.)
                try
                {
                    var subOk = await this._events.ConnectAndSubscribeAsync(baseUrl, token, CancellationToken.None)
                                             .ConfigureAwait(false);
                    PluginLog.Info($"{LogPrefix} Event subscription success={subOk}");
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, $"{LogPrefix} Event subscription threw");
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} EnsureHaReady exception");
                return false;
            }
            finally
            {
                _haConnectGate.Release();
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

                if (!ps.TryGetString(ControlScript, out var entityId) || String.IsNullOrWhiteSpace(entityId))
                {
                    PluginLog.Warning($"{LogPrefix} RunCommand: No script selected");
                    return false;
                }

                var hasVars = ps.TryGetString(ControlVarsJson, out var rawVars) && !String.IsNullOrWhiteSpace(rawVars);
                var preferToggle = ps.TryGetBoolean(ControlUseToggle, out var toggle) && toggle;

                PluginLog.Info($"{LogPrefix} Press: entity='{entityId}' preferToggle={preferToggle} hasVars={hasVars}");

                if (preferToggle && hasVars)
                {
                    // script.toggle ignores variables — make that explicit in logs
                    PluginLog.Warning($"{LogPrefix} Toggle selected but variables provided — variables will be ignored.");
                }

                // Toggle path (no variables)
                if (preferToggle)
                {
                    var (ok, err) = this._client.CallServiceAsync("script", "toggle", entityId, data: null, CancellationToken.None)
                                           .GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} call_service script.toggle '{entityId}' -> ok={ok} err='{err}'");
                    return ok;
                }

                // If running, stop it
                var isRunning = _isRunningCache.TryGetValue(entityId, out var r) && r;
                PluginLog.Info($"{LogPrefix} Current running state: {isRunning}");

                if (isRunning)
                {
                    var (ok, err) = this._client.CallServiceAsync("script", "turn_off", entityId, data: null, CancellationToken.None)
                                           .GetAwaiter().GetResult();
                    PluginLog.Info($"{LogPrefix} call_service script.turn_off '{entityId}' -> ok={ok} err='{err}'");
                    return ok;
                }

                // Else start it (with optional variables)
                JsonElement? serviceData = null;
                if (hasVars)
                {
                    try
                    {
                        using var varsDoc = JsonDocument.Parse(rawVars);
                        var wrapper = new Dictionary<String, Object> { ["variables"] = varsDoc.RootElement };
                        var wrapperJson = JsonSerializer.Serialize(wrapper);
                        using var wrapperDoc = JsonDocument.Parse(wrapperJson);
                        serviceData = wrapperDoc.RootElement.Clone();

                        PluginLog.Info($"{LogPrefix} Variables parsed OK: {SafeJson(varsDoc.RootElement)}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning(ex, $"{LogPrefix} Variables JSON invalid. Will run without variables. raw='{rawVars}'");
                        serviceData = null;
                    }
                }

                var (ok2, err2) = this._client.CallServiceAsync("script", "turn_on", entityId, serviceData, CancellationToken.None)
                                         .GetAwaiter().GetResult();
                PluginLog.Info($"{LogPrefix} call_service script.turn_on '{entityId}' data={SafeJson(serviceData)} -> ok={ok2} err='{err2}'");
                return ok2;
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

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(ControlScript))
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
                    if (String.IsNullOrEmpty(id) || !id.StartsWith("script.", StringComparison.OrdinalIgnoreCase))
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

                    e.AddItem(name: id, displayName: display, description: "Home Assistant script");
                    count++;
                }

                PluginLog.Info($"{LogPrefix} List populated with {count} script(s)");

                // keep current selection
                var current = e.ActionEditorState?.GetControlValue(ControlScript) as String;
                if (!String.IsNullOrEmpty(current))
                {
                    PluginLog.Info($"{LogPrefix} Keeping current selection: '{current}'");
                    e.SetSelectedItemName(current);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading scripts", ex.Message);
            }
        }

        // ---- helpers ----
        private static String SafeJson(JsonElement? elem)
        {
            try
            {
                if (elem.HasValue)
                {
                    return JsonSerializer.Serialize(elem.Value);
                }
            }
            catch { }
            return "null";
        }

        private static String SafeJson(JsonElement elem)
        {
            try
            {
                return JsonSerializer.Serialize(elem);
            }
            catch { return "null"; }
        }
    }
}

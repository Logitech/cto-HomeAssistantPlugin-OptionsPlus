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
        private HaEventListener _events;

        private const String ItemLoading = "!loading";
        private const String ItemNone = "!none";


        private static readonly ConcurrentDictionary<String, String> _scripts =
    new(StringComparer.OrdinalIgnoreCase);

        private static volatile Boolean _scriptsLoadedOnce = false;
        private static readonly SemaphoreSlim _scriptsRefreshGate = new(1, 1);

        private const String ControlScript = "ha_script";
        private const String ControlVarsJson = "ha_vars_json";
        private const String ControlUseToggle = "ha_use_toggle";

        private readonly IconService _icons;

        // Cache "running" state of scripts (kept updated by HaEventListener)
        private static readonly ConcurrentDictionary<String, Boolean> _isRunningCache =
            new(StringComparer.OrdinalIgnoreCase);



        // Gate to avoid concurrent connect races
        private static readonly SemaphoreSlim _haConnectGate = new(1, 1);

        public RunScriptAction()
        {
            this.Name = "HomeAssistant.RunScript";
            this.DisplayName = "HA: Run Script";
            this.GroupName = "Home Assistant";
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
            this.ActionEditor.ControlValueChanged += this.OnActionEditorControlValueChanged;


            this._icons = new IconService(new Dictionary<String, String>
            {
                { IconId.RunScript, "run_script_icon.png" },
            });

            // Fire-and-forget prefetch with a short timeout to be ready for the first open
            this.ActionEditor.Started += async (_, __) =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    _ = this.RefreshScriptsCacheAsync(cts.Token); // fire-and-forget
                };
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters parameters, Int32 width, Int32 height) =>
            this._icons.Get(IconId.RunScript);

        protected override Boolean OnLoad()
        {
            PluginLog.Info($"{LogPrefix} OnLoad()");
            if (this.Plugin is HomeAssistantPlugin p)
            {
                this._client = p.HaClient;
                this._events = p.HaEvents;

                // Fire-and-forget prefetch with a short timeout to be ready for the first open
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                _ = this.RefreshScriptsCacheAsync(cts.Token);

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
                // If we have cached items, serve them immediately (works offline too)
                if (!_scripts.IsEmpty)
                {
                    var count = 0;
                    foreach (var kv in _scripts)
                    {
                        e.AddItem(name: kv.Key, displayName: kv.Value, description: "Home Assistant script");
                        count++;
                    }
                    PluginLog.Info($"{LogPrefix} Served cached scripts: {count}");

                    // Keep current selection if still valid
                    var current = e.ActionEditorState?.GetControlValue(ControlScript) as String;
                    if (!String.IsNullOrEmpty(current) && _scripts.ContainsKey(current))
                    {
                        e.SetSelectedItemName(current);
                    }
                    return;
                }

                // If we've tried before and still nothing, show a "no scripts" item
                if (_scriptsLoadedOnce && _scripts.IsEmpty)
                {
                    e.AddItem(ItemNone, "No scripts found", "Define scripts in Home Assistant, then refresh.");
                    return;
                }

                // First time / no cache yet → show a placeholder immediately (no blocking!)
                e.AddItem(ItemLoading, "Loading… close and reopen this menu", "Fetching scripts from Home Assistant");

                // Kick a background load with a short timeout; UI stays responsive
                Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    var ok = await this.RefreshScriptsCacheAsync(cts.Token).ConfigureAwait(false);
                    PluginLog.Info($"{LogPrefix} Background prefetch after list request ok={ok}");
                });
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, $"{LogPrefix} List population failed");
                e.AddItem("!error", "Error reading scripts", ex.Message);
            }
        }



        private void OnActionEditorControlValueChanged(Object _, ActionEditorControlValueChangedEventArgs ce)
        {
            if (!ce.ControlName.EqualsNoCase(ControlScript))
            {
                return;
            }

            var v = ce.ActionEditorState.GetControlValue(ControlScript);
            if (!String.Equals(v, ItemLoading, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Try a refresh in the background; user can reopen the list to see results
            Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var ok = await this.RefreshScriptsCacheAsync(cts.Token).ConfigureAwait(false);

                // Give immediate feedback; reopening the dropdown will re-query and hit the cache
                this.Plugin.OnPluginStatusChanged(
                    ok ? PluginStatus.Normal : PluginStatus.Error,
                    ok ? "Scripts loaded — reopen the list." : "Couldn’t reach Home Assistant."
                );
            });
        }


        private async Task<Boolean> RefreshScriptsCacheAsync(CancellationToken ct)
        {
            await _scriptsRefreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // If not connected yet, connect first
                if (!await this.EnsureHaReadyAsync().ConfigureAwait(false))
                {
                    return false;
                }

                var (ok, json, error) = await this._client.RequestAsync("get_states", ct).ConfigureAwait(false);
                if (!ok || String.IsNullOrEmpty(json))
                {
                    PluginLog.Warning($"{LogPrefix} RefreshScriptsCacheAsync: get_states failed: '{error ?? "unknown"}'");
                    return false;
                }

                var local = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
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
                    local[id] = display;
                }

                // swap into concurrent cache
                _scripts.Clear();
                foreach (var kv in local)
                {
                    _scripts[kv.Key] = kv.Value;
                }

                _scriptsLoadedOnce = true;
                PluginLog.Info($"{LogPrefix} Refreshed scripts: count={_scripts.Count}");
                return true;
            }
            catch (OperationCanceledException)
            {
                PluginLog.Warning($"{LogPrefix} RefreshScriptsCacheAsync: canceled/timeout");
                return false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{LogPrefix} RefreshScriptsCacheAsync exception");
                return false;
            }
            finally
            {
                _scriptsRefreshGate.Release();
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
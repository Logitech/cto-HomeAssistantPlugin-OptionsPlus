namespace Loupedeck.HomeAssistantPlugin
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using Loupedeck;

    public sealed class ConfigureHomeAssistantAction : ActionEditorCommand
    {
        // ====================================================================
        // CONSTANTS - Configure Home Assistant Action Configuration
        // ====================================================================

        // --- Connection Configuration ---
        private const Int32 DefaultHomeAssistantPort = 8123;          // Default Home Assistant port
        private const Int32 ConnectionTestTimeoutSeconds = 8;         // Timeout for connection testing

        private const String CtlBaseUrl = "BaseUrl";
        private const String CtlToken = "Token";
        private const String CtlTest = "Test";

        public ConfigureHomeAssistantAction()
        {
            this.Name = "ConfigureHomeAssistant";
            this.DisplayName = "Configure Home Assistant";
            this.GroupName = "Home Assistant";
            this.Description = "Drop into a tile to configure Home assistant before the first use(can remove after saving).";

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(CtlBaseUrl, "Base URL (prefer wss for enhanced security):")
                    .SetPlaceholder($"wss://homeassistant.local:{DefaultHomeAssistantPort}/"));

            this.ActionEditor.AddControlEx(
                new ActionEditorTextbox(CtlToken, "Long-Lived Token:")
                    .SetRequired()
                    .SetPlaceholder("Paste token from HA Profile → Create Token"));

            this.ActionEditor.AddControlEx(new ActionEditorButton(CtlTest, "Test connection"));

            // ✅ Use events – you get ActionEditorState here
            this.ActionEditor.Started += this.OnActionEditorStarted;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }


        protected override Boolean OnLoad() => true;

        // This action is just a config surface, not something to "run"
        protected override Boolean RunCommand(ActionEditorActionParameters _) => true;

        private void OnActionEditorStarted(Object? sender, ActionEditorStartedEventArgs e)
        {
            // Reflect current config in the editor header
            if (this.Plugin.TryGetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, out var baseUrl) &&
                !String.IsNullOrWhiteSpace(baseUrl))
            {
                try
                {
                    var host = new Uri(baseUrl).Host;
                    e.ActionEditorState.SetDisplayName($"Configure Home Assistant ({host})");
                }
                catch
                {
                    e.ActionEditorState.SetDisplayName("Configure Home Assistant");
                }
            }
            else
            {
                e.ActionEditorState.SetDisplayName("Configure Home Assistant");
            }
        }

        private void OnControlValueChanged(Object? sender, ActionEditorControlValueChangedEventArgs e)
        {
            try
            {
                if (e.ControlName.EqualsNoCase(CtlBaseUrl))
                {
                    var v = e.ActionEditorState.GetControlValue(CtlBaseUrl)?.Trim();
                    if (!String.IsNullOrEmpty(v))
                    {
                        this.Plugin.SetPluginSetting(HomeAssistantPlugin.SettingBaseUrl, v, false);

                        // ✅ Update the name live from the new URL
                        try
                        {
                            var host = new Uri(v).Host;
                            e.ActionEditorState.SetDisplayName($"Configure Home Assistant ({host})");
                        }
                        catch
                        {
                            e.ActionEditorState.SetDisplayName("Configure Home Assistant");
                        }
                    }
                }
                else if (e.ControlName.EqualsNoCase(CtlToken))
                {
                    var v = e.ActionEditorState.GetControlValue(CtlToken);
                    if (!String.IsNullOrEmpty(v))
                    {
                        this.Plugin.SetPluginSetting(HomeAssistantPlugin.SettingToken, v, false);
                    }
                }
                else if (e.ControlName.EqualsNoCase(CtlTest))
                {
                    var baseUrl = e.ActionEditorState.GetControlValue(CtlBaseUrl);
                    var token = e.ActionEditorState.GetControlValue(CtlToken);

                    if (String.IsNullOrWhiteSpace(baseUrl) || String.IsNullOrWhiteSpace(token))
                    {
                        this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Enter Base URL and Token first.");
                        return;
                    }

                    // Immediate feedback
                    this.Plugin.OnPluginStatusChanged(PluginStatus.Warning, "Testing connection...");

                    Task.Run(async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ConnectionTestTimeoutSeconds));
                        var client = new HaWebSocketClient();
                        var (ok, msg) = await client.ConnectAndAuthenticateAsync(baseUrl, token, TimeSpan.FromSeconds(ConnectionTestTimeoutSeconds), cts.Token);
                        await client.SafeCloseAsync();

                        // Update UI on completion
                        this.Plugin.OnPluginStatusChanged(ok ? PluginStatus.Normal : PluginStatus.Error,
                            ok ? "HA auth OK." : msg ?? "Auth failed.");
                    });
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "ConfigureHomeAssistantAction value change failed");
                this.Plugin.OnPluginStatusChanged(PluginStatus.Error, "Error saving or testing settings.");
            }
        }
    }
}